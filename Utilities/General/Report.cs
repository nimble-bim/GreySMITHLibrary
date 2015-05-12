﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

using Autodesk.Revit.Exceptions;

namespace GreySMITH.Utilities.General
{
    public static class ExceptionReport
    {
        public static void Log(Exception e)
        {
            // automatically flush out the last lines when complete
            Debug.AutoFlush = true;

            // print the bulk of the exception info
            Debug.WriteLine("The program failed here:");
            Debug.WriteLine(e.Message);
            Debug.WriteLine(e.Data);
            Debug.WriteLine(e.StackTrace);

            // if there are any InnerExceptions, recursively print those as well
            if(e.InnerException != null)
            {
                ExceptionReport.Log(e.InnerException);
            }
        }

        public static void Log(Exception e, string specificmessage)
        {
            // prints a specific message before actually doing the report log
            Debug.Print(specificmessage);
            ExceptionReport.Log(e);
        }
    }
}
