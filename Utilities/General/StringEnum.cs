﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

namespace GreySMITH.Utilities.General
{
    public static class StringEnum
    {
        public static string GetStringValue(this Enum value)
        {
            string output = null;

            try
            {
                Type type = value.GetType();

                FieldInfo fi = type.GetField(value.ToString());
                StringValueAttribute[] attrs =
                   fi.GetCustomAttributes(typeof(StringValueAttribute),
                                           false) as StringValueAttribute[];
                if (attrs.Length > 0)
                {
                    output = attrs[0].Value;
                }
            }

            catch (Exception e)
            {
                // exception handling code
            }

            return output;
        }
    }
}
