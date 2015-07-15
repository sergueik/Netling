using System;
using System.Collections;

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Data;
using System.Data.SQLite;
using SQLite.Utils;

using System.Linq;
using System.Text;
using System.Windows;
using Core.Models;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace Client
{
    public partial class ResultWindow : Window
    {

        private static string dataSource;
        private static string tableName; 
        public JobResult<UrlResult> Result { get; private set; }

        public ResultWindow(JobResult<UrlResult> result)
        {
            InitializeComponent();
            Result = result;

            TotalRequests.Text = result.Count.ToString(CultureInfo.InvariantCulture);
            RequestsPerSecond.Text = string.Format("{0:0}", result.JobsPerSecond);
            ResponseTime.Text = string.Format("{0:0}", result.Results.Where(r => !r.IsError).DefaultIfEmpty(new UrlResult(0, 0, DateTime.Now, null, 0)).Average(r => r.ResponseTime));
            Elapsed.Text = string.Format("{0:0}", result.ElapsedMilliseconds);
            Bandwidth.Text = string.Format("{0:0}", Math.Round(result.BytesPrSecond * 8 / 1024 / 1024, MidpointRounding.AwayFromZero));
            Errors.Text = result.Errors.ToString(CultureInfo.InvariantCulture);

            //Title = string.Format("{0} threads, {1:0.#} seconds duration & {2} URLs", result.Threads, result.ElapsedMilliseconds / 1000, result.Results.Select(r => r.Url).Distinct().Count());

            LoadUrlSummary();
            LoadGraphs();

        }

        private void LoadGraphs()
        {
            long startTime = 0;

            if (Result.Results.Any())
                startTime = Result.Results.First().StartTime.Ticks;

            var result = Result.Results
                .Where(r => !r.IsError)
                .GroupBy(r => ((r.StartTime.Ticks - startTime) / 10000 + r.ResponseTime) / 1000)
                .OrderBy(r => r.Key)
                .Select(r => new DataPoint(r.Key, r.Count()));

            RequestsPerSecondGraph.Draw(result);

            var i = 1;
            var ms = Result.Results
                .Where(r => !r.IsError)
                .OrderByDescending(r => r.ResponseTime)
                .Select(r => new DataPoint(i++, r.ResponseTime));

            ResponseTimeGraph.Draw(ms);
        }

        private void LoadUrlSummary()
        {
            var list = new List<SummaryResult>();
            foreach (var url in Result.Results.Select(r => r.Url).Distinct())
            {
                var urlResult = Result.Results.Where(r => r.Url == url).ToList();
                var responseTime = urlResult.Where(r => !r.IsError).DefaultIfEmpty(new UrlResult(0, 0, DateTime.Now, null, 0)).Average(r => r.ResponseTime);

                list.Add(new SummaryResult
                {
                    Url = url,
                    ResponseTime = (int)responseTime,
                    Errors = urlResult.Count(r => r.IsError),
                    Size = string.Format("{0:0.0}", urlResult.Where(r => !r.IsError).DefaultIfEmpty(new UrlResult(0, 0, DateTime.Now, null, 0)).Average(r => r.Bytes) / 1024)
                });
            }

            UrlSummary.ItemsSource = list;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = string.Format("Result-{0:yyyy.MM.dd_HHmm}", DateTime.Now),
                DefaultExt = ".db",
                Filter = "SQLite Database (.db)|*.db"
            };


            if (dialog.ShowDialog() == true)
            {

               
            dataSource = "data source=" + dialog.FileName;
            tableName = "product";
            
            createTable();
            TestConnection();
                var i = 1;
                var m =
                 Result.Results
                    .Where(r => !r.IsError)
                    .OrderByDescending(r => r.ResponseTime)
                    .Select(r => new DataPoint(i++, r.ResponseTime));
                foreach (var row in m)
                {
                    var dic = new Dictionary<string, object>(){
            	{"count" ,row.X} ,
                	{"responsetime", row.Y}
                };

                    insert(dic);
                }

                ResponseTimeGraph.Background = System.Windows.Media.Brushes.Red;
                var ms = DataLoad();
                ResponseTimeGraph.Draw(ms);

            }
        }


        public IEnumerable<DataPoint> DataLoad()
        {
            try
            {
            	string sql = String.Format("select * from {0}", tableName) ;
                using (SQLiteConnection conn = new SQLiteConnection(dataSource))
                {
                    using (SQLiteCommand cmd = new SQLiteCommand())
                    {
                        cmd.Connection = conn;
                        conn.Open();
                        SQLiteHelper sh = new SQLiteHelper(cmd);
                        DataTable res = sh.Select(sql);                        
                        IEnumerable<DataPoint> ms = from row in res.AsEnumerable()
                                  orderby row["responsetime"]
                                  select new DataPoint(Convert.ToDouble(row["count"]), Convert.ToDouble(row["responsetime"]));
                        ;

                        conn.Close();

                        return ms;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return (IEnumerable<DataPoint>)null;
            }

        }
        public bool insert(Dictionary<string, object> dic)
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(dataSource))
                {
                    using (SQLiteCommand cmd = new SQLiteCommand())
                    {
                        cmd.Connection = conn;
                        conn.Open();
                        SQLiteHelper sh = new SQLiteHelper(cmd);
                        sh.Insert(tableName, dic);
                        conn.Close();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return false;
            }
        }

                public static void createTable()
        {
            using (SQLiteConnection conn = new SQLiteConnection(dataSource))
            {
                using (SQLiteCommand cmd = new SQLiteCommand())
                {
                    cmd.Connection = conn;
                    conn.Open();
                    SQLiteHelper sh = new SQLiteHelper(cmd);
                    sh.DropTable(tableName);

                    SQLiteTable tb = new SQLiteTable(tableName);
                    tb.Columns.Add(new SQLiteColumn("id", true)); // auto increment 
                    tb.Columns.Add(new SQLiteColumn("count"));
                    tb.Columns.Add(new SQLiteColumn("responsetime", ColType.Decimal));
                    sh.CreateTable(tb);
                    conn.Close();
                }
            }
        }

        bool TestConnection()
        {
            Console.WriteLine(String.Format("Testing database source connection`n{0}...", dataSource));
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(dataSource))
                {
                    conn.Open();
                    conn.Close();
                }
                return true;
            }

            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return false;
            }
        }

    }

    public class SummaryResult
    {
        public string Url { get; set; }
        public string Size { get; set; }
        public int ResponseTime { get; set; }
        public int Errors { get; set; }
    }

}
