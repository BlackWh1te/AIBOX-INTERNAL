using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace AIBoxInternal
{
    public static class SymbolDumper
    {
        public static void Dump()
        {
            string path = Path.Combine(Application.dataPath, "../worldbox_internal_log.txt");
            using (StreamWriter sw = new StreamWriter(path))
            {
                sw.WriteLine("// AIBox Internal - Symbol Dump (Pseudo-Offsets)");
                sw.WriteLine("// Generated at: " + DateTime.Now.ToString());
                sw.WriteLine();

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name != "Assembly-CSharp") continue;

                    sw.WriteLine($"// --- Assembly: {assembly.GetName().Name} ---");
                    
                    foreach (Type type in assembly.GetTypes())
                    {
                        sw.WriteLine($"class {type.FullName} // Token: 0x{type.MetadataToken:X}");
                        
                        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                        {
                            sw.WriteLine($"  [Offset: 0x{method.MetadataToken:X}] {method.ReturnType.Name} {method.Name}({GetParameters(method)})");
                        }
                        
                        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                        {
                            sw.WriteLine($"  [Field: 0x{field.MetadataToken:X}] {field.FieldType.Name} {field.Name}");
                        }
                        sw.WriteLine();
                    }
                }
            }
            Debug.Log("[AIBox-Internal] Dump completed to worldbox_internal_log.txt");
        }

        private static string GetParameters(MethodInfo method)
        {
            List<string> parms = new List<string>();
            foreach (var p in method.GetParameters())
            {
                parms.Add($"{p.ParameterType.Name} {p.Name}");
            }
            return string.Join(", ", parms);
        }
    }
}
