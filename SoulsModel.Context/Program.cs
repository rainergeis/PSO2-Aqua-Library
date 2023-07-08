using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;

namespace SoulsModel.Context
{
    [SupportedOSPlatform("windows")]
    class Program
    {
        static void Main(string[] args)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Console.Write(
                $"{assembly.GetName().Name} {assembly.GetName().Version}\n\n" +
                "This program will register SoulsModelTool.exe\n" +
                "so that it can be run by right-clicking on a file or folder.\n" +
                "Enter R to register, U to unregister, or anything else to exit.\n" +
                "> ");
            string choice = Console.ReadLine().ToUpper();
            Console.WriteLine();

            if (choice == "R" || choice == "U" || choice == "L")
            {
                try
                {
                    RegistryKey classes = Registry.CurrentUser.OpenSubKey("Software\\Classes", true);
                    if (choice == "R")
                    {
                        string soulsmodelbinderPath = Path.GetFullPath("SoulsModelTool.exe");
                        RegistryKey soulsModelFileKey = classes.CreateSubKey("*\\shell\\SoulsModel");
                        RegistryKey soulsModelFileCommand = soulsModelFileKey.CreateSubKey("command");
                        soulsModelFileKey.SetValue(null, "SoulsModelTool");
                        soulsModelFileCommand.SetValue(null, $"\"{soulsmodelbinderPath}\" \"%1\"");
                        RegistryKey soulsModelDirKey = classes.CreateSubKey("directory\\shell\\SoulsModel");
                        RegistryKey soulsModelDirCommand = soulsModelDirKey.CreateSubKey("command");
                        soulsModelDirKey.SetValue(null, "SoulsModelTool");
                        soulsModelDirCommand.SetValue(null, $"\"{soulsmodelbinderPath}\" \"%1\"");
                        
                        Console.WriteLine("Programs registered!");
                    }
                    else if (choice == "U")
                    {
                        classes.DeleteSubKeyTree("*\\shell\\SoulsModel", false);
                        classes.DeleteSubKeyTree("directory\\shell\\SoulsModel", false);
                        Console.WriteLine("Programs unregistered.");
                    }
                    else if (choice == "L") 
                    {
                        //MultipleInvokePromptMinimum
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Operation failed; try running As Administrator. Reason:\n{ex}");
                }

                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
            }
        }
    }
}
