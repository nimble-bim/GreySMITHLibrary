﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Configuration;
using System.Reflection;
using Autodesk.Revit.Attributes;
using NLog;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using GreySMITH.Revit.Commands.Wrappers;
using GreySMITH.Revit.Extensions.Elements;
using GreySMITH.Revit.Extensions.Documents;

namespace GreySMITH.Revit.Commands
{

    /// <summary>
    /// Command designed to allow the user to "rough out" the piping for multiple plumbing fixtures simultaneously
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public partial class DrawPipeOutCommand : AbstractCommand
    {
        public DrawPipeOutCommand()
        {
            
        }

        // default constructor for concrete classes - make sure to implement this in all cases.
        public DrawPipeOutCommand(
            ExternalCommandData excmd,
            string mainmessage,
            ElementSet elemset,
            string classname,
            string description,
            string assemblyLocation,
            string panelname = "Plumbing",
            string commandname = "Draw Roughing")
            : base(excmd, mainmessage, elemset, commandname, panelname, assemblyLocation, classname, description)
        {
            _mainMessage = mainmessage;
            _externalCMD = excmd;
            _elementSet = elemset;
        }

        private static readonly Logger Logger =
            LogManager.GetCurrentClassLogger();
        private List<ElementId> ListOfElementsWithoutConnectors =
            new List<ElementId>();
        private Dictionary<Connector, Element> _connectorDictionary =
            new Dictionary<Connector, Element>();

        public Dictionary<Connector, Element> ConnectorDictionary
        {
            get
            {
                return _connectorDictionary;
            }
        }
        private void CreateConnectorDictionary()
        {
            IEnumerable<Connector> fecConnectors =
                new FilteredElementCollector(CurrentDocument).OfType<Connector>();

            foreach (Connector connector in fecConnectors)
            {
                _connectorDictionary.Add(connector, connector.Owner);
            }
        }
        public override Result Work()
        {
            // create a dictionary of elements in the model vs connectors
            CreateConnectorDictionary();

            // select the object(s)
            IList<Element> plumbingFixtures =
                PromptUserToSelectObjects();

            // draw a pipe from the object's connection of the correct system type in the appropiate direction
            DrawRoughing(plumbingFixtures);
            
            return Result.Failed;
        }
        private IList<Element> PromptUserToSelectObjects()
        {
            IList<Element> selectedElements = new List<Element>();
            using (Selection selected = UiDocument.Selection)
            {
                IEnumerable<Reference> refObjects = selected.PickObjects(ObjectType.Element);
                
                // for some reason this doesn't work with ElementIds?
                //IEnumerable<ElementId> selectedElementIds =
                //    refObjects.SelectMany<Reference, ElementId>(x => x.ElementId);

                foreach(Reference r in refObjects)
                {
                    ElementId referenceId = r.ElementId;
                    selectedElements.Add(CurrentDocument.GetElement(referenceId));
                }
            }

            return selectedElements;
        }
        private bool HasConnection(Element element)
        {
            if (_connectorDictionary.ContainsValue(element))
                return true;
            return false;
        }
        private Result DrawRoughing(IList<Element> elementList)
        {
            foreach (Element element in elementList)
            {
                DrawPipeToOrFromObject(element);
            }

            TaskDialog.Show(
                "Roughing Results",
                string.Format(
                    "{0} objects were unable to be drawn because of a lack of connectors. " +
                    "Speak to the architect and creator of the family so" +
                    "that a connector can be added to the family", 
                    ListOfElementsWithoutConnectors.Count()));

            return Result.Succeeded;
        }
        private void DrawPipeToOrFromObject(Element element)
        {
            // find the object
            XYZ elementLocation =       
                ((LocationPoint) element.Location).Point;

            // if there are no connectors - keep track and let the user know to add a connector for that fixture
            if (!HasConnection(element))
            {
                Logger.Debug("ElementID {0} contained no connections and was neither drawn to nor from.", element.Id);
                ListOfElementsWithoutConnectors.Add(element.Id);
                return;
            }
            
            // try to draw a connector for all the connections in the model
            foreach (Connector c in (ConnectorDictionary.Keys.Where(c => c.Owner == element)))
            {
                DrawPipeToOrFromConnector(c);
            }
        }
        private void DrawPipeToOrFromConnector(Connector c)
        {
            using (Transaction trPipeDraw = new Transaction(CurrentDocument, "Drawing pipe..."))
            {
                trPipeDraw.Start();

                // calculate the distance the pipe should be drawn
                double amount = CalculateRoughing(c.Owner);

                // based on the pipe systems in the document, suggest the most likely pipe to be used
                PipeType suggestedPipeType = FindSuggestedPipeType(CurrentDocument, c.PipeSystemType);

                // find out if the connector direction will intersect with object
                // if so, pipe should start away from connector and connect to it
                XYZ pipeStartPoint = CalculatePipeDirection(IntersectsWithOwner(c), c, amount);
                
                // draw the pipe
                DrawPipe(c, suggestedPipeType, pipeStartPoint);

                trPipeDraw.Commit();
            }
        }
        //TODO: Does the pipe derive it's size from the connector's size?
        private void DrawPipe(Connector connector, PipeType pipeType, XYZ pipeStartPoint)
        {
            CurrentDocument.Create.NewPipe(pipeStartPoint, connector, pipeType);
        }
        private XYZ CalculatePipeDirection(bool fromOppositeDirection, Connector connector, double pipeLength)
        {
            // normal of connector
            var normalPoint = connector.CoordinateSystem.BasisZ;
            var originPoint = connector.CoordinateSystem.Origin;
            // want to convert normal to something else?
            
            // if so draw FROM THE OPPOSITE DIRECTION, pipe system is supply.

            // use this method to compare the connector's normal direction with the document's XYZ

            return new XYZ();
        }
        //TODO: Write a test to ensure that this returns the most frequent pipe AND standard pipe if none are available
        //TODO: What if there are no pipes in the document at all? Find a way to suggest a standard pipe
        /// <summary>
        /// Returns a PipeType based on the PipeType most used with a specific System Type in this document
        /// </summary>
        /// <param name="currentDocument">Document to check</param>
        /// <param name="pipeSystemType">The pipe system type to check for</param>
        /// <returns>The most used pipe type for this system type</returns>
        private PipeType FindSuggestedPipeType(Document currentDocument, PipeSystemType pipeSystemType )
        {
            // find all the runs of pipe which have the same PipeSystemType as above
            var pipes = from pipe in (new FilteredElementCollector(currentDocument).OfType<Pipe>())
                        where ((PipingSystem)pipe.MEPSystem).SystemType.Equals(pipeSystemType)
                        select pipe;

            // find the most frequently used PipeType in this Pipe System
            var pipeType = pipes.GroupBy(p => p.PipeType).
                           OrderByDescending(type => type.Count()).
                           Take(1).
                           Select(mostFrequent => mostFrequent.Key).ToArray().First();

            // return the pipe system type which occurs the most in the group
            return pipeType;

        }
        //TODO: Write a test to ensure that this returns Imperial when Imperial and Metric when not
        //TODO: Write a test to ensure this returns a simple value when there is no host
        private double CalculateRoughing(Element element)
        {
            double roughingAmount = 12.0;

            // if the element has a host AND that host is not a level
            if (((FamilyInstance)element).Host != null &&
                ((FamilyInstance)element).Host.GetType() != typeof(Level)) { 
                return CalculateRoughingFromHost(element);}

            // returns an number that is already converted to the Document's internal Unit System
            // i.e: changes inches to millimeters or vice versa
            return UnitUtils.ConvertToInternalUnits(roughingAmount, DisplayUnitType.DUT_FRACTIONAL_INCHES);
        }
        //TODO: Write a test for each of these cases
        private double CalculateRoughingFromHost(Element element)
        {
            // typical roughing amount in inches
            double roughingAmount = 12.0;

            // variable for the host width which will be returned
            double hostWidth = 0;

            // the element's host (Host is typically a Wall, Roof, Ceiling or Floor, but could be something else)
            Element hostElement = ((FamilyInstance) element).Host;

            // define the Host Type
            switch (hostElement.Category.Name)
            {
                case "ExtrusionRoof":
                    hostWidth = ((RoofBase) hostElement).FasciaDepth;
                    break;

                case "FootPrintRoof":
                    hostWidth = ((FootPrintRoof) hostElement).FasciaDepth;
                    break;

                case "Wall":
                    hostWidth = ((Wall) hostElement).Width;
                    break;

                case "Ceiling":
                    hostWidth = ((Ceiling) hostElement).ParametersMap.get_Item("Thickness").AsDouble();
                    break;

                case "Floor":
                    hostWidth = ((Floor) hostElement).ParametersMap.get_Item("Thickness").AsDouble();
                    break;

                default:
                    Logger.Warn(
                        "The element called :'{0}' has an unknown host type called '{1}'." +
                        " Create a new case for this in the method.",
                        element.Name, hostElement.Name);
                    break;
            }

            return UnitUtils.ConvertToInternalUnits(roughingAmount, DisplayUnitType.DUT_FRACTIONAL_INCHES) + hostWidth;
        }
        //TODO: Write a test to ensure this actually intersects itself
        private bool IntersectsWithOwner(Connector c)
        {
            ReferenceWithContext intersectedReference = 
                new ReferenceIntersector(                           // shoots a ray from the connector's face
                c.Owner.Id,                                         // element it looks for intersection with
                FindReferenceTarget.All,                            // look for all intersections because this should hit it's owner first anyway if they intersect
                CurrentDocument.Create3DView()).                    // newly created 3DView
                FindNearest(c.Origin, c.CoordinateSystem.BasisZ);   // gives the normal from the connector's face

            // if the object returned is not null AND is the element itself, the connector intersects with it
            if (null != intersectedReference.GetReference() &&
                intersectedReference.GetReference() == new Reference(c.Owner)) { 
                return true;}

            return false;
        }
        #region Un-Ready Methods
        /// <summary>
        /// This might have to wait - realizing that it would be quite a task
        /// to programmatically place the connector because the user would
        /// expect it come out of very specific places for each piece of geometry
        /// </summary>
        /// <param name="instanceOfObject">object to add connector to</param>
        private void AddConnector(FamilyInstance instanceOfObject)
        {
            Document familyDocument = instanceOfObject.Symbol.Document;

            using (Transaction tr_AddConnector = new Transaction(familyDocument))
            {
                tr_AddConnector.Start("Adding connector to the current family");



            }
        }
        #endregion
    }
}
