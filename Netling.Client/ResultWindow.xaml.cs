﻿using System;
using System.Collections;

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Data;
using SQLite.Utils;

using System.Linq;
using System.Text;
using System.Windows;
using Netling.Core.Models;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using NUnit.Framework;
    
namespace Netling.Client
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

            TotalRequests.Text = string.Format("{0:#,0}", result.Count);
            RequestsPerSecond.Text = string.Format("{0:#,0}", result.JobsPerSecond);
            Elapsed.Text = string.Format("{0:#,0}", result.ElapsedMilliseconds);
            Bandwidth.Text = string.Format("{0:0}", Math.Round(result.BytesPrSecond * 8 / 1024 / 1024, MidpointRounding.AwayFromZero));
            Errors.Text = string.Format("{0:#,0}", result.Errors);

            var avgResponseTime = result.Results.Where(r => !r.IsError).DefaultIfEmpty(new UrlResult(0, 0, DateTime.Now, null, 0)).Average(r => r.ResponseTime);

            if (avgResponseTime > 5)
                ResponseTime.Text = string.Format("{0:#,0}", avgResponseTime);
            else
                ResponseTime.Text = string.Format("{0:0.00}", avgResponseTime);

            LoadUrlSummary();
            LoadGraphs();

        }

        private void LoadGraphs()
        {
            long startTime = 0;
            var max = (int)Math.Floor(Result.ElapsedMilliseconds / 1000);

            if (Result.Results.Any())
                startTime = Result.Results.First().StartTime.Ticks;

            var result = Result.Results
                .Where(r => !r.IsError)
                .GroupBy(r => ((r.StartTime.Ticks - startTime) / 10000 + (int)r.ResponseTime) / 1000)
                .Where(r => r.Key < max)
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
            
            PlotHelper.createTable(dataSource,tableName);
            PlotHelper.TestConnection(dataSource);
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

                    PlotHelper.InsertPlotData(dataSource,tableName, dic);
                }

                //ResponseTimeGraph.Background = System.Windows.Media.Brushes.Red;
                //var ms = PlotHelper.LoadData(dataSource,tableName);
                //ResponseTimeGraph.Draw(ms);

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
