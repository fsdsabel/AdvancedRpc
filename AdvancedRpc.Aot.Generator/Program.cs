using CommandLine;
using Microsoft.Build.Evaluation;
using System;
using System.Collections.Generic;
using System.IO;

namespace AdvancedRpc.Aot.Generator
{
    public class Program
    {
        public class Options
        {
            [Value(0, Min = 1, HelpText = "Input files to scan for Interfaces. Might be csproj or cs.")]
            public IEnumerable<string> Filenames { get; set; }

            [Option('o', "outfile", Required = true, HelpText = "File to write the generated proxies to.")]
            public string OutFile { get; set; }

            [Option("verbose", Default = false)]
            public bool Verbose { get; set; }
        }

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(Convert)
                .WithNotParsed(e=>
                {
                    Console.Error.WriteLine(string.Join("\n", e));
                });
            
        }

        public static void Convert(Options options)
        {
            var parser = new InterfaceParser();
            foreach(var file in options.Filenames)
            {
                if (string.Equals(".cs", Path.GetExtension(file), StringComparison.InvariantCultureIgnoreCase))
                {
                    parser.AddSourceFile(file);
                    if (options.Verbose)
                    {
                        Console.WriteLine($"Scanning source file '{file}'");
                    }
                }
                else if (string.Equals(".csproj", Path.GetExtension(file), StringComparison.InvariantCultureIgnoreCase))
                {
                    var project = Project.FromFile(file, new Microsoft.Build.Definition.ProjectOptions());
                    
                    foreach(var item in project.GetItems("Compile"))
                    {
                        parser.AddSourceFile(Path.Combine(project.DirectoryPath, item.EvaluatedInclude));
                    }

                    ProjectCollection.GlobalProjectCollection.UnloadProject(project);
                }
            }

            File.WriteAllText(options.OutFile, parser.ParseSources(new ProxyGenerator()));
        }

    }
}
