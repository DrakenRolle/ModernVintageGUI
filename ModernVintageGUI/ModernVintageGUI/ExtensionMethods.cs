using IS2Mod.ControlTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace IS2Mod
{
    public static class ExtensionMethods
    {
        public static bool IsOrInheritsFrom(this Type instanceType, Type type)
        {
            return instanceType == type || instanceType.IsSubclassOf(type);
        }
    }
}
