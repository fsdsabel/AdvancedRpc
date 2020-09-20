using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AdvancedRpc.Aot.Generator
{

    internal class InterfaceParser
    {
        private readonly List<string> _files = new List<string>();
                
        public void AddSourceFile(string file)
        {
            _files.Add(file);
        }

        public string ParseSources(ProxyGenerator proxyGenerator)
        {
            var result = new System.Text.StringBuilder();
            var syntaxTrees = new List<SyntaxTree>();
            foreach (var file in _files)
            {
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(file))));
            }

            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

            var compilation = CSharpCompilation.Create("Parsing", syntaxTrees, new[] { mscorlib });

            foreach (var tree in syntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);

                var aotInterfaces = tree.GetRoot().DescendantNodes()
                    .OfType<InterfaceDeclarationSyntax>()
                     .Where(i => i.AttributeLists.Any(al => al.Attributes.Any(a => a.Name.ToString() == "AotRpcObject" || a.Name.ToString() == "AdvancedRpcLib.AotRpcObject")))
                    .ToArray();

                foreach(var intf in aotInterfaces)
                {
                    result.AppendLine(proxyGenerator.CreateProxy(intf, model));                    
                }
            }

            return proxyGenerator.CreateFinalSource(result.ToString());
        }
    }
}