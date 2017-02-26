﻿using System;
using System.Collections.Generic;
using System.Linq;
//using MoreLinq;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Shared;
using fi = Shared.Filter;
using ut = Shared.Util;
using op = Shared.Output;
using tr = Shared.Transformation;
using mp = Shared.MyMepUtils;

namespace PlaceSupport
{
    public class PlaceSupport
    {
        public static Tuple<Pipe, Element> PlaceSupports(ExternalCommandData commandData)
        {
            var app = commandData.Application;
            var uiDoc = app.ActiveUIDocument;
            var doc = uiDoc.Document;

            try
            {
                //Select a pipe
                var selectedPipe = ut.SelectSingleElementOfType(uiDoc, typeof(Pipe),
                    "Select a pipe where to place a support!", false);
                //Get end connectors
                var conQuery = (from Connector c in mp.GetALLConnectorsFromElements(selectedPipe)
                               where (int)c.ConnectorType == 1
                               select c).ToList();

                Connector c1 = conQuery.First();
                Connector c2 = conQuery.Last();
                //Define a plane by three points
                var plane = Plane.CreateByThreePoints(c1.Origin, c2.Origin, new XYZ(c1.Origin.X + 5, c1.Origin.Y, c1.Origin.Z));
                //Set view sketch plane to the be the created plane
                var sp = SketchPlane.Create(doc, plane);
                uiDoc.ActiveView.SketchPlane = sp;
                //Get a 3d point by picking a point
                XYZ point_in_3d = null;
                try
                {
                    point_in_3d = uiDoc.Selection.PickPoint(
                      "Please pick a point on the plane" 
                      + " defined by the selected face");
                }
                catch (OperationCanceledException)
                {
                }
                
                //Get family symbol
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                ElementParameterFilter filter = fi.ParameterValueFilter("Support Symbolic: ANC", BuiltInParameter.SYMBOL_FAMILY_AND_TYPE_NAMES_PARAM);
                LogicalOrFilter classFilter = fi.FamSymbolsAndPipeTypes();
                FamilySymbol familySymbol = (FamilySymbol)collector.WherePasses(classFilter).WherePasses(filter).FirstOrDefault();
                if (familySymbol == null) throw new Exception("No SUPPORT FamilySymbol loaded in project!");

                //Get the host pipe level
                Level level = (Level)doc.GetElement(selectedPipe.LevelId);

                //Create the support instance
                Element support = doc.Create.NewFamilyInstance(point_in_3d, familySymbol, level, StructuralType.NonStructural);

                //Get the connector from the support
                ConnectorSet connectorSetToAdd = mp.GetConnectorSet(support);
                if (connectorSetToAdd.IsEmpty)
                    throw new Exception("The support family lacks a connector. Please read the documentation for correct procedure of setting up a support element.");
                Connector connectorToConnect = (from Connector c in connectorSetToAdd select c).FirstOrDefault();

                //Rotate into place
                tr.RotateElementInPosition(point_in_3d, connectorToConnect, c1, support);

                //Set diameter
                Parameter nominalDiameter = support.LookupParameter("Nominal Diameter");
                nominalDiameter.Set(conQuery.First().Radius * 2);

                return new Tuple<Pipe, Element>((Pipe)selectedPipe, support);
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }
        }
    }
}
