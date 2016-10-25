﻿#region Namespaces
using System;
using System.Reflection;
using System.IO;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using cn = ConnectConnectors.ConnectConnectors;
using Document = Autodesk.Revit.Creation.Document;

#endregion

namespace MyRibbonPanel
{
    [Transaction(TransactionMode.Manual)]
    class App : IExternalApplication
    {
        public const string myRibbonPanelToolTip = "My Own Ribbon Panel";

        //Method to get the button image
        BitmapImage NewBitmapImage(Assembly a, string imageName)
        {
            Stream s = a.GetManifestResourceStream(imageName);

            BitmapImage img = new BitmapImage();

            img.BeginInit();
            img.StreamSource = s;
            img.EndInit();

            return img;
        }

        // get the absolute path of this assembly
        static string ExecutingAssemblyPath = Assembly.GetExecutingAssembly().Location;
        // get ref to assembly
        Assembly exe = Assembly.GetExecutingAssembly();

        public Result OnStartup(UIControlledApplication application)
        {
            AddMenu(application);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private void AddMenu(UIControlledApplication application)
        {
            RibbonPanel rvtRibbonPanel = application.CreateRibbonPanel("MyRevitAddins");
            PushButtonData data = new PushButtonData("ConnectConnectors", "Connect Connectors", ExecutingAssemblyPath, "MyRibbonPanel.ConnectConnectors");
            data.ToolTip = myRibbonPanelToolTip;
            data.Image = NewBitmapImage(exe, "MyRibbonPanel.ImgConnectConnectors16.png");
            data.LargeImage = NewBitmapImage(exe, "MyRibbonPanel.ImgConnectConnectors32.png");
            PushButton pushButton = rvtRibbonPanel.AddItem(data) as PushButton;
        }

        [Transaction(TransactionMode.Manual)]
        class ConnectConnectors : IExternalCommand
        {
            public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            {
                try
                {
                    

                    using (Transaction trans = new Transaction(commandData.Application.ActiveUIDocument.Document))
                    {
                        trans.Start("Connect the Connectors!");
                        cn.ConnectTheConnectors(commandData);
                        trans.Commit();
                    }

                    return Result.Succeeded;
                }

                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

                catch (Exception ex)
                {
                    message = ex.Message;
                    return Result.Failed;
                }
            }
        }
    }
}
