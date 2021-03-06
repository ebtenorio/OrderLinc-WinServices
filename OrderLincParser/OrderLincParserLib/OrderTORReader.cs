﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using System.IO;
using System.Diagnostics;
using PL.PersistenceServices;
using PL.PersistenceServices.DTOS;
using PL.PersistenceServices.Enumerations;
using OrderLinc.DTOs;
using OrderLinc.Services;
using OrderLinc;
using OrderLinc.IDataContracts;

namespace OrderLinc.EDIFileProcessor
{
    public class OrderTORReader
    {
        private Timer mTimer = new Timer();

        private string mSenderCode;
        private string mReceiverCode;
        private string mOrderID;
        // private string mOrderNumber;

        OrderLincServices _orderLincService;
        IConfigurationService _configService;
        IOrderService _orderService;
        ILogService _logService;
        string source = "OrderLinc Order Parser";

        // reads TOR from the speficied folders
        public OrderTORReader(OrderLincServices orderLincService)
        {
            try
            {
                _orderLincService = orderLincService;
                _configService = _orderLincService.ConfigurationService;
                _orderService = _orderLincService.OrderService;
                _logService = _orderLincService.LogService;
            }
            catch (Exception ex)
            {
                this.WriteEvent("OrderTOOReader Constructor", ex);
                throw ex;
            }
        }

        public void Start()
        {
            try
            {
                double interval = 30;

                DTOSYSConfig config = _configService.SYSConfigListByKey("ParserInterval");
                if (config != null && config.ConfigValue != null)
                    interval = double.Parse(config.ConfigValue);

                mTimer.Elapsed += new ElapsedEventHandler(OnAppTimerElapsed);
                mTimer.Interval = interval * 1000;
                mTimer.Enabled = true;
                mTimer.Start();
                _logService.LogSave("OrderTORReader", "Service Started", 0);
            }
            catch (Exception ex)
            {
                this.WriteEvent("StartService()", ex);
                _logService.LogSave("OrderTORReader", "Error StartService " + ex, 0);
                throw ex;
            }
        }

        public void Stop()
        {
            EventLog.WriteEntry("StopService()", "Reader StopService Invoked");
            mTimer.Stop();
            mTimer.Enabled = false;
        }

        private void OnAppTimerElapsed(object source, ElapsedEventArgs e)
        {
            try
            {
                mTimer.Stop();

                this.ProcessTOORFiles();
                mTimer.Start();

            }
            catch (Exception ex)
            {
                mTimer.Start();
                this.WriteEvent("OnAppTimerElapsed()", ex);
                throw ex;
            }
            finally
            {
                mTimer.Start();

            }
        }

        private void ProcessTOORFiles()
        {
            try
            {
                List<string> files = new List<string>();
                DTOOrder order = new DTOOrder();

                files = this.TOORFiles();
                if (files.Count > 0)
                {
                    string completedFolder = GetConfigValue("Completed");

                    foreach (string @file in files)
                    {
                        this.ReadTORFile(@file);
                        //order = _OrderAppLib.OrderService.MatchTIR(mSenderCode, mReceiverCode, mOrderID);
                        order = _orderService.OrderListBySenderReceiver(long.Parse(mOrderID), mSenderCode, mReceiverCode);

                        if (order != null)
                        {
                            if (order.OrderID != 0)
                            {
                                DTOSYSOrderStatus orderStatus = _orderService.OrderStatusListByCode("ACK");

                                //order.SYSOrderStatusID = _OrderAppLib.OrderService.GetSYSOrderStatusID("ACK");

                                if (orderStatus == null)
                                    throw new ArgumentOutOfRangeException(string.Format("Order status not found : {0}", "ACK"));
                                order.SYSOrderStatusID = orderStatus.SYSOrderStatusID;

                                _orderService.OrderUpdateStatus(order);
                                //_OrderAppLib.OrderService.OrderUpdateStatus (order);

                                // this.MoveFile(@file, @_OrderAppLib.OrderService.GetConfigValue("Completed") + @"\" + Path.GetFileName(file)); // move file to rejected
                                this.MoveFile(@file, completedFolder + @"\" + Path.GetFileName(file)); // move file to rejected
                            }
                            else
                            {
                                // R, this is the new line of code
                                this.MoveFile(@file, GetConfigValue("Rejected") + @"\" + Path.GetFileName(file)); // move file to rejected
                            }
                        }
                        else
                        {
                            //this.MoveFile(@file, @_OrderAppLib.OrderService.GetConfigValue("Rejected") + @"\" + Path.GetFileName(file)); // move file to rejected
                            this.MoveFile(@file, GetConfigValue("Rejected") + @"\" + Path.GetFileName(file)); // move file to rejected

                            _logService.LogSave("ProcessTOORFiles", "The Order Number : "+ mOrderID + "  cannot be matched to an Order", 0);
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                this.WriteEvent("ProcessTOORFiles()", ex);
                _logService.LogSave("ProcessTOORFiles", "Error - " + ex, 0);
                throw ex;
            }
        }

        private string GetConfigValue(string configKey)
        {
            DTOSYSConfig config = _configService.SYSConfigListByKey(configKey);

            if (config == null)
                throw new ArgumentNullException(string.Format("ConfigKey not found '{0}'.", configKey));

            return config.ConfigValue;
        }

        private Boolean ReadTORFile(string @FileName)
        {
            Boolean _IsRead = false;
            string _FileContent;
            string[] _ContentByLine = null;
            string[] _ContentByDelimeter = null;

            try
            {
                _FileContent = File.ReadAllText(@FileName);
                _ContentByLine = _FileContent.Split('~');
                _ContentByDelimeter = _ContentByLine[0].Split('|'); // FILE HEADER LINE
                mReceiverCode = _ContentByDelimeter[5].ToString();
                mSenderCode = _ContentByDelimeter[4].ToString();

                _ContentByDelimeter = _ContentByLine[1].Split('|'); // ORDER HEADER LINE
                mOrderID = _ContentByDelimeter[6].ToString();
                _IsRead = true;

            }
            catch (FileNotFoundException ex)
            {
                this.WriteEvent("ReadTOORFile()", ex);
                throw ex;
            }

            return _IsRead;
        }

        private Boolean MoveFile(string @SourceFile, string @DestinationFile)
        {
            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(@DestinationFile)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(@DestinationFile));
                }

                File.Move(@SourceFile, @DestinationFile);
            }
            catch (Exception ex)
            {
                this.WriteEvent("MoveFile()", ex);
                throw ex;

            }

            return true;
        }

        private List<string> TOORFiles()
        {
            List<string> _tmpTOORfiles = new List<string>();
            if (!Directory.Exists(GetConfigValue("Received")))
                Directory.CreateDirectory(GetConfigValue("Received"));
            string[] files = Directory.GetFiles(GetConfigValue("Received"));

            foreach (string @file in @files)
            {
                _tmpTOORfiles.Add(@file);
            }

            return _tmpTOORfiles;
        }

        private Boolean WriteEvent(String MethodName, Exception exception)
        {
            EventLog.WriteEntry("OrderAppParserService", "Method Name:" + MethodName + Environment.NewLine +
                                                         "Error Message: " + exception.Message + Environment.NewLine);
            return true;
        }
    }
}
