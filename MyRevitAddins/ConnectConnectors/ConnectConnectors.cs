﻿using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
//using MoreLinq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Shared;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using fi = Shared.Filter;
using ut = Shared.Util;
using op = Shared.Output;
using mp = Shared.MyMepUtils;
//using mySettings = GeneralStability.Properties.Settings;

namespace ConnectConnectors
{
    public class ConnectConnectors
    {
        public static void ConnectTheConnectors(ExternalCommandData commandData)
        {
            var app = commandData.Application;
            var uiDoc = app.ActiveUIDocument;
            var doc = uiDoc.Document;
            var selection = uiDoc.Selection.GetElementIds();
            var elements = new HashSet<Element>(from ElementId id in selection select doc.GetElement(id));
            var connectors = mp.GetALLConnectors(elements);
            //Find duplicate
            bool foundIt = false;
            while (!foundIt)
            {
                if (connectors.Count < 2) throw new Exception("No eligible connectors found! Check alignment.");
                Connector c1 = connectors[0];
                connectors.RemoveAt(0);
                foreach (Connector c2 in connectors.Where(c2 => ut.IsEqual(c1.Origin, c2.Origin)))
                {
                    c1.ConnectTo(c2);
                    break;
                }
            }
        }
    }
}

