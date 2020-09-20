using DotLiquid;
using DotLiquid.FileSystems;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AdvancedRpc.Aot.Generator
{
    class ProxyGenerator : IFileSystem
    {
        private readonly List<InterfaceModel> _models = new List<InterfaceModel>();

        public ProxyGenerator()
        {
            Template.FileSystem = this;
        }

        public string CreateProxy(InterfaceDeclarationSyntax intf, SemanticModel model)
        {
            var template = Template.Parse(Template.FileSystem.ReadTemplateFile(null, "ProxyClass"));
            var intfModel = new InterfaceModel(intf, model);
            _models.Add(intfModel);
            return template.Render(Hash.FromAnonymousObject(new
            {
                Interface = intfModel
            }));            
        }

        public string CreateFinalSource(string content)
        {
            var template = Template.Parse(Template.FileSystem.ReadTemplateFile(null, "ProxyFile"));
            return template.Render(Hash.FromAnonymousObject(new
            {
                Content = content,
                Interfaces = _models
            }));
        }

        public string ReadTemplateFile(Context context, string templateName)
        {
            using(var s = typeof(ProxyGenerator).Assembly.GetManifestResourceStream(typeof(ProxyGenerator), $"Templates.{templateName}.liquid"))
            {
                using(var reader = new StreamReader(s))
                {
                    return reader.ReadToEnd();
                }
            }
            throw new FileNotFoundException(templateName);
        }

        
    }

    
    class InterfaceModel : Drop
    {        
        public InterfaceModel(InterfaceDeclarationSyntax intf, SemanticModel model)
        {
            var symbol = model.GetDeclaredSymbol(intf) as INamedTypeSymbol;
            Name = symbol.Name;
            ProxyName = Name + "RpcShadow";
            Namespace = symbol.ContainingNamespace.Name;


            var members = symbol.GetMembers().Concat(
                        symbol.AllInterfaces.SelectMany(i => i.GetMembers()))
                .ToArray();

            Methods = members.OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary)
                .Select(m => new MethodModel(m)).ToArray();

            Properties = members.OfType<IPropertySymbol>()                
                .Select(m => new PropertyModel(m)).ToArray();

            Events = members.OfType<IEventSymbol>()
                .Select(e => new EventModel(e)).ToArray();

            ImplementedInterface = symbol.ToString();
            AllImplementedInterfaces = string.Join(", ", new[] { $"typeof({ImplementedInterface })" }
                .Concat(symbol.AllInterfaces.Select(i => $"typeof({i})")));
        }

        public string Namespace { get; } 

        public string Name { get; }

        public string ProxyName { get; }

        public MethodModel[] Methods { get; }

        public PropertyModel[] Properties { get; }

        public EventModel[] Events { get; }

        public string ImplementedInterface { get; }

        public string AllImplementedInterfaces { get; }
    }

    class EventModel : Drop
    {
        public EventModel(IEventSymbol e)
        {
            Name = e.Name;
            Type = e.Type.ToString();
            AddName = e.AddMethod.Name;
            RemoveName = e.RemoveMethod.Name;
        }

        public string Name { get; }
        public string Type { get; }
        public string AddName { get; }
        public string RemoveName { get; }
    }

    class PropertyModel : Drop
    {
        public PropertyModel(IPropertySymbol p)
        {
            ReturnType = p.Type.ToString();
            Name = p.Name;
            HasGetter = p.GetMethod != null;
            HasSetter = p.SetMethod != null;
            GetterName = p.GetMethod.Name;
            SetterName = p.SetMethod?.Name;
        }

        public string ReturnType { get; }

        public string Name { get; }

        public bool HasGetter { get; }
        public bool HasSetter { get; }
        public string GetterName { get; }
        public string SetterName { get; }
    }

    class MethodModel : Drop
    {
        public MethodModel(IMethodSymbol m)
        {
            ReturnType = m.ReturnType.ToString();
            Name = m.Name;
            Parameters = m.Parameters.Select(p => new ParameterModel(p)).ToArray();
        }
        public string ReturnType { get; }

        public string Name { get; }

        public ParameterModel[] Parameters { get; }
    }

    class ParameterModel : Drop
    {
        public ParameterModel(IParameterSymbol p)
        {
            Name = p.Name;
            Type = p.Type.ToString();
        }

        public string Name { get; }

        public string Type { get; }

        public override string ToString()
        {
            return $"{Type} {Name}";
        }
    }
}