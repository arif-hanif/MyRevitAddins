﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using MoreLinq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Shared;
using Autodesk.Revit.DB;
using fi = Shared.Filter;
using ut = Shared.Util;
using op = Shared.Output;
using mySettings = GeneralStability.Properties.Settings;
using TxBox = System.Windows.Forms.TextBox;

namespace GeneralStability
{
    public class InteractionRevit
    {
        public FamilyInstance Origo { get; } //Holds the Origo family instance
        public WallData WallsAlong { get; }
        public WallData WallsCross { get; }
        public BoundaryData BoundaryData { get; }
        public LoadData LoadData { get; }


        public InteractionRevit(Document doc)
        {
            //Get the Origo component
            Origo = GetOrigo(doc);

            //Gather the detail components
            WallsAlong = new WallData("GS_Stabilizing_Wall: Stabilizing Wall - Along", Origo, doc);
            WallsCross = new WallData("GS_Stabilizing_Wall: Stabilizing Wall - Cross", Origo, doc);

            //Initialize boundary data
            BoundaryData = new BoundaryData("GS_Boundary", doc);

            //Initialize load data
            LoadData = new LoadData(doc);
        }

        #region LoadCalculation

        public Result CalculateLoads(Document doc, TxBox txBox)
        {
            try
            {
                //Log
                int nrI = 0, nrJ = 0, nrTotal = 1;
                //StringBuilder sbLog = new StringBuilder();

                //Reset the load from last run
                foreach (FamilyInstance fi in WallsAlong.WallSymbols)
                {
                    fi.LookupParameter("GS_Load").Set(0);
                }

                //Get the transform
                Transform trfO = Origo.GetTransform();
                Transform trf = trfO.Inverse;

                //Get the list of boundaries and walls (to simplify the synthax)
                HashSet<CurveElement> Bd = BoundaryData.BoundaryLines;
                HashSet<FamilyInstance> Walls = WallsAlong.WallSymbols;

                //Combine the two sets to make a combination
                HashSet<Element> wallsAndBoundaries = (from CurveElement cu in Bd
                                                       where !StartPoint(cu, trf).X.Equals(EndPoint(cu, trf).X)
                                                       select cu).Cast<Element>().ToHashSet();

                wallsAndBoundaries.UnionWith(Walls);

                //Get the faces of filled regions
                //Determine the intersection between the centre point of finite element and filled region symbolizing the load
                IList<Face> faces = new List<Face>();

                Options options = new Options();
                options.ComputeReferences = true;
                options.View = LoadData.GS_View;

                //Optimization: order filled regions by size: Descending = Largest first
                var LoadAreas = LoadData.LoadAreas.OrderByDescending(x => GetFace(x, options).Area).ToList();

                foreach (FilledRegion fr in LoadAreas) faces.Add(GetFace(fr, options));

                //The analysis proceeds in steps (hardcoded for now)
                double step = 20.0.MmToFeet(); //<-- a "magic" number. TODO: Implement definition of step size in UI.

                foreach (FamilyInstance fi in Walls)
                {

                    //Determine the start X
                    double Xmin = StartPoint(fi, trf).X;

                    //Determine the end X
                    double Xmax = EndPoint(fi, trf).X;

                    //The y of the wall
                    double Ycur = StartPoint(fi, trf).Y;

                    ////Divide the largest X value by the step value to determine the number iterations in X direction
                    int nrOfX = (int)Math.Floor((Xmax - Xmin) / step);

                    //Iterate through the length of the current wall analyzing the load
                    for (int i = 0; i < nrOfX; i++)
                    {
                        //Current x value
                        double x1 = Xmin + i * step;
                        double x2 = Xmin + (i + 1) * step;
                        double xC = x1 + step / 2;

                        //Determine relevant elements (that means the walls and boundaries which are crossed by the current X value iteration)
                        var elementsX = (from Element el in wallsAndBoundaries
                                         where StartPoint(el, trf).X <= xC && EndPoint(el, trf).X >= xC
                                         select el).OrderByDescending(x => StartPoint(x, trf).Y);

                        //Determine relevant walls (that means the walls which are crossed by the current X value iteration)
                        var wallsX = (from FamilyInstance fin in Walls
                                      where StartPoint(fin, trf).X <= xC && EndPoint(fin, trf).X >= xC
                                      select fin).OrderByDescending(x => StartPoint(x, trf).Y);

                        //Determine relevant walls (that means the walls which are crossed by the current X value iteration)
                        var boundaryX = (from CurveElement cue in Bd
                                         where StartPoint(cue, trf).X <= xC && EndPoint(cue, trf).X >= xC
                                         select cue).ToHashSet();

                        //First handle the walls
                        var wallsXlinked = new LinkedList<FamilyInstance>(wallsX);
                        var listNode1 = wallsXlinked.Find(fi);
                        var wallPositive = listNode1.Next.Value;
                        var wallNegative = listNode1.Previous.Value;

                        //Flow control
                        bool isEdgePositive = false, isEdgeNegative = false; //<-- Indicates if the wall is on the boundary

                        //Select boundaries if no walls found at location
                        CurveElement bdPositive = null, bdNegative = null;
                        if (wallPositive == null) bdPositive = boundaryX.MaxBy(x => StartPoint(x, trf).Y);
                        if (wallNegative == null) bdNegative = boundaryX.MinBy(x => StartPoint(x, trf).Y);

                        //Detect edge cases
                        if (wallPositive == null && StartPoint(bdPositive, trf).Y.Equals(Ycur)) isEdgePositive = true;
                        if (wallNegative == null && StartPoint(bdNegative, trf).Y.Equals(StartPoint(fi, trf).Y)) isEdgeNegative = true;

                        int nrOfYPos, nrOfYNeg;

                        //Determine number of iterations in Y direction POSITIVE handling all cases
                        //The 2* multiplier on step makes sure that iteration only happens on the half of the span
                        if (wallPositive != null) nrOfYPos = (int)Math.Floor((StartPoint(wallPositive, trf).Y - Ycur) / 2 * step);
                        else if (isEdgePositive) nrOfYPos = 0;
                        else nrOfYPos = (int)Math.Floor((StartPoint(bdPositive, trf).Y - Ycur) / 2 * step);

                        //Determine number of iterations in Y direction NEGATIVE handling all cases
                        //The 2* multiplier on step makes sure that iteration only happens on the half of the span
                        if (wallNegative != null) nrOfYNeg = (int)Math.Floor((-StartPoint(wallNegative, trf).Y + Ycur) / 2 * step);
                        else if (isEdgeNegative) nrOfYNeg = 0;
                        else nrOfYNeg = (int)Math.Floor((-StartPoint(bdNegative, trf).Y + Ycur) / 2 * step);

                        //Iterate through the POSITIVE side
                        for (int j = 0; j < nrOfYPos; j++)
                        {
                            //ITERATE ONLY THROUGH THE HALF OF WIDTH!!!!
                        }

                    }


                    //Iterate through the width of the building

                    {
                        //Current y value
                        double y1 = Ymin + j * step;
                        double y2 = Ymin + (j + 1) * step;
                        double yC = y1 + step / 2;

                        //Determine nearest wall
                        FamilyInstance nearestWall = wallsX.MinBy(x => Math.Abs(StartPoint(x, trf).Y - yC));

                        //Area of finite element
                        double areaSqrM = ((x2 - x1) * (y2 - y1)).SqrFeetToSqrMeters();

                        //Determine the correct load intensity at the finite element centre point
                        XYZ cPointInOrigoCoords = new XYZ(xC, yC, 0);
                        XYZ cPointInGlobalCoords = trfO.OfPoint(cPointInOrigoCoords);

                        double loadIntensity = 0.0;

                        for (int f = 0; f < faces.Count; f++)
                        {
                            IntersectionResult result = faces[f].Project(cPointInGlobalCoords);
                            if (result != null)
                            {
                                string rawLoadIntensity =
                                    LoadAreas[f].get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString();
                                loadIntensity = double.Parse(rawLoadIntensity, CultureInfo.InvariantCulture);
                                break;
                            }
                        }

                        double force = loadIntensity * areaSqrM;

                        double currentValue = nearestWall.LookupParameter("GS_Load").AsDouble();

                        double load = force + currentValue;

                        nearestWall.LookupParameter("GS_Load").Set(load);

                        nrTotal++;

                    }
                }

                //Log
                txBox.Text = nrI + ", " + nrJ + ": " + nrTotal;
                //op.WriteDebugFile(mySettings.Default.debugFilePath, sbLog);
                return Result.Succeeded;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw new Exception(e.Message);
                return Result.Failed;
            }
        }

        #endregion


        /// <summary>
        /// Renumbers the wall symbols by following geometric pattern sorting by y and then x of start point.
        /// </summary>
        /// <param name="doc"></param>
        /// <returns></returns>
        public static Result RenumberWallSymbols(Document doc)
        {
            try
            {
                //Get the origo family
                FamilyInstance Origo = GetOrigo(doc);

                //Get and assign number to walls along
                string name = "GS_Stabilizing_Wall: Stabilizing Wall - Along";
                HashSet<FamilyInstance> along = GetWallSymbolsUnordered(name, doc);
                IList<FamilyInstance> wallsAlongSorted = OrderGeometrically(along, Origo);

                int idx = 0;
                foreach (FamilyInstance fi in wallsAlongSorted)
                {
                    idx++;
                    fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set(idx.ToString());
                }

                name = "GS_Stabilizing_Wall: Stabilizing Wall - Cross";
                HashSet<FamilyInstance> cross = GetWallSymbolsUnordered(name, doc);
                IList<FamilyInstance> wallsCrossSorted = OrderGeometrically(cross, Origo);

                idx = 0;
                foreach (FamilyInstance fi in wallsCrossSorted)
                {
                    idx++;
                    fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set(idx.ToString());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        /// <summary>
        /// Orders the LocationCurve based FamilyInstances according to their Y value then their X value.
        /// </summary>
        /// <param name="listToOrder">The list of FamilyInstances to order.</param>
        /// <param name="Origo">The reference FamilyInstance in whose coordinate system the sortin must be done.</param>
        /// <returns></returns>
        private static IList<FamilyInstance> OrderGeometrically(HashSet<FamilyInstance> listToOrder, FamilyInstance Origo)
        {
            Transform trf = Origo.GetTransform();
            trf = trf.Inverse;
            return (from FamilyInstance fi in listToOrder
                    orderby StartPoint(fi, trf).Y.FtToMeters(), StartPoint(fi, trf).X.FtToMeters()
                    select fi).ToList();
        }

        /// <summary>
        /// Returns the start point of the LocationCurve transformed to the reference coordinates.
        /// </summary>
        /// <param name="fi">LocationCurve based FamilyInstance.</param>
        /// <param name="trf">Inverse Transform of the reference coordinates.</param>
        private static XYZ StartPoint(FamilyInstance fi, Transform trf)
        {
            if (fi == null) return null;
            LocationCurve loc = fi.Location as LocationCurve;
            return trf.OfPoint(loc.Curve.GetEndPoint(0));
        }

        /// <summary>
        /// Returns the start point of the CurveElement transformed to the reference coordinates.
        /// </summary>
        /// <param name="cu">CurveElement such as detail line.</param>
        /// <param name="trf">Inverse Transform of the reference coordinates.</param>
        private static XYZ StartPoint(CurveElement cu, Transform trf)
        {
            return trf.OfPoint(cu.GeometryCurve.GetEndPoint(0));
        }

        /// <summary>
        /// Returns the start point of the Element transformed to the reference coordinates.
        /// </summary>
        /// <param name="el">Elements to retrieve from, it will be cast to proper type.</param>
        /// <param name="trf">Inverse Transform of the reference coordinates.</param>
        private static XYZ StartPoint(Element el, Transform trf)
        {
            if (el is FamilyInstance) return StartPoint((FamilyInstance)el, trf);
            if (el is CurveElement) return StartPoint((CurveElement)el, trf);
            throw new Exception("Type not handled!");
        }

        /// <summary>
        /// Returns the end point of the LocationCurve transformed to the reference coordinates.
        /// </summary>
        /// <param name="fi">LocationCurve based FamilyInstance.</param>
        /// <param name="trf">Inverse Transform of the reference coordinates.</param>
        private static XYZ EndPoint(FamilyInstance fi, Transform trf)
        {
            LocationCurve loc = fi.Location as LocationCurve;
            return trf.OfPoint(loc.Curve.GetEndPoint(1));
        }

        /// <summary>
        /// Returns the end point of the CurveElement transformed to the reference coordinates.
        /// </summary>
        /// <param name="cu">CurveElement such as detail line.</param>
        /// <param name="trf">Inverse Transform of the reference coordinates.</param>
        private static XYZ EndPoint(CurveElement cu, Transform trf)
        {
            return trf.OfPoint(cu.GeometryCurve.GetEndPoint(1));
        }

        /// <summary>
        /// Returns the end point of the Element transformed to the reference coordinates.
        /// </summary>
        /// <param name="el">Elements to retrieve from, it will be cast to proper type.</param>
        /// <param name="trf">Inverse Transform of the reference coordinates.</param>
        private static XYZ EndPoint(Element el, Transform trf)
        {
            if (el is FamilyInstance) return EndPoint((FamilyInstance)el, trf);
            if (el is CurveElement) return EndPoint((CurveElement)el, trf);
            throw new Exception("Type not handled!");
        }

        private static FamilyInstance GetOrigo(Document doc)
        {
            FilteredElementCollector colOrigo = new FilteredElementCollector(doc);
            return colOrigo
                .WherePasses(fi.FamInstOfDetailComp())
                .WherePasses(fi.ParameterValueFilter("GS_Origo: Origin",
                    BuiltInParameter.SYMBOL_FAMILY_AND_TYPE_NAMES_PARAM)).Cast<FamilyInstance>()
                .FirstOrDefault();
        }

        private static HashSet<FamilyInstance> GetWallSymbolsUnordered(string familyName, Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            return collector.WherePasses(fi.FamInstOfDetailComp())
                .WherePasses(fi.ParameterValueFilter(familyName,
                    BuiltInParameter.SYMBOL_FAMILY_AND_TYPE_NAMES_PARAM))
                .Cast<FamilyInstance>()
                .ToHashSet();
        }

        private static Face GetFace(FilledRegion fr, Options options)
        {
            GeometryElement geometryElement = fr.get_Geometry(options);
            foreach (GeometryObject go in geometryElement)
            {
                Solid solid = go as Solid;
                return solid.Faces.get_Item(0);
            }
            return null;
        }
    }

    public class WallData
    {
        public HashSet<FamilyInstance> WallSymbols { get; }
        public IList<double> Length { get; } = new List<double>();
        public IList<double> X { get; } = new List<double>();
        public IList<double> Y { get; } = new List<double>();
        public IList<double> Thickness { get; } = new List<double>();

        //private readonly string _debugFilePath = mySettings.Default.debugFilePath;

        public WallData(string familyName, FamilyInstance Origo, Document doc)
        {
            //Get the relevant wall symbols
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            WallSymbols = collector.WherePasses(fi.FamInstOfDetailComp())
                .WherePasses(fi.ParameterValueFilter(familyName,
                    BuiltInParameter.SYMBOL_FAMILY_AND_TYPE_NAMES_PARAM))
                .OrderBy(x => int.Parse(x.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).AsString())) //Mark must be filled with integer numbers
                .Cast<FamilyInstance>()
                .ToHashSet();

            //Analyze the geometry to get x and y values
            //Obtain the transform from the Origo family
            Transform trf = Origo.GetTransform();
            trf = trf.Inverse;

            //StringBuilder sb = new StringBuilder();

            foreach (FamilyInstance fi in WallSymbols)
            {
                //Get the location points of the wall symbols
                LocationCurve loc = fi.Location as LocationCurve;

                //Collect the length of the walls
                Length.Add(loc.Curve.Length.FtToMeters());

                //Get the end and start points
                Curve locCurve = loc.Curve;
                XYZ start = locCurve.GetEndPoint(0);
                XYZ end = locCurve.GetEndPoint(1);

                //Transform the points
                XYZ tStart = trf.OfPoint(start);
                XYZ tEnd = trf.OfPoint(end);

                double sX = tStart.X.FtToMeters(), sY = tStart.Y.FtToMeters(), eX = tEnd.X.FtToMeters(), eY = tEnd.Y.FtToMeters();

                //sb.Append("Wall ("+ sX +","+ sY + ") <> ");
                //sb.Append("(" + eX + "," + eY + ") <> ");
                //sb.Append(loc.Curve.Length.FtToMeters());
                //sb.AppendLine();

                //Take advantage of the fact that X or Y is equal
                if (sX.Equals(eX)) X.Add(sX);
                else if (sY.Equals(eY)) Y.Add(sY);
                else throw new Exception("No equal coordinates found!!!\n");

                //Collect the width of the wall from the wall symbol
                Thickness.Add(fi.LookupParameter("GS_Width").AsDouble().FtToMillimeters());
            }

            //op.WriteDebugFile(_debugFilePath, sb);
        }
    }

    public class BoundaryData
    {
        public HashSet<CurveElement> BoundaryLines { get; }
        public HashSet<XYZ> Vertices { get; } = new HashSet<XYZ>();

        public BoundaryData(string lineName, Document doc)
        {
            BoundaryLines = (from CurveElement cu in fi.GetElements<CurveElement>(doc)
                             where cu.LineStyle.Name == lineName
                             select cu).ToHashSet();

            #region Collect Vertices

            //Get the end points of boundary lines, but discard duplicates
            foreach (CurveElement cu in BoundaryLines)
            {
                Curve curve = cu.GeometryCurve;
                //Start point
                XYZ p1 = curve.GetEndPoint(0);
                if (!Vertices.Any(p => p.IsEqual(p1))) Vertices.Add(p1);
                //End point
                XYZ p2 = curve.GetEndPoint(1);
                if (!Vertices.Any(p => p.IsEqual(p2))) Vertices.Add(p2);
            }

            //I think this statement sorts the points CCW
            //http://stackoverflow.com/questions/22435397/sort-2d-points-in-a-list-clockwise
            //http://stackoverflow.com/questions/6996942/c-sharp-sort-list-of-x-y-coordinates-clockwise?rq=1

            double cX = 0, cY = 0;

            //Adds all x and y coords
            cX = Vertices.Aggregate(cX, (current, pt) => current + pt.X) / Vertices.Count;
            cY = Vertices.Aggregate(cY, (current, pt) => current + pt.Y) / Vertices.Count;
            //Define centre point
            XYZ cp = new XYZ(cX, cY, 0);
            //Sorts the points -> Works only for convex hulls!
            Vertices = Vertices.OrderByDescending(pt => Math.Atan2(pt.X - cp.X, pt.Y - cp.Y)).ToHashSet();

            #endregion

        }
    }

    public class LoadData
    {
        public IList<FilledRegion> LoadAreas { get; }
        public ViewPlan GS_View { get; }

        public LoadData(Document doc)
        {
            GS_View = fi.GetViewByName<ViewPlan>("GeneralStability", doc); //<-- this is a "magic" string. TODO: Find a better way to specify the view, maybe by using the current view.

            LoadAreas = fi.GetElements<FilledRegion>(doc, GS_View.Id).ToList();
        }

    }

}
