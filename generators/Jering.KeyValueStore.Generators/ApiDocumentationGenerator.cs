using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

#nullable enable

namespace Jering.KeyValueStore.Generators
{
    // TODO
    // - inline generator documentation
    [Generator]
    public class ApiDocumentationGenerator : SourceGenerator<ApiDocumentationGeneratorSyntaxReceiver>
    {
        private static readonly DiagnosticDescriptor _missingClassDeclaration = new("G0007",
            "Missing class declaration",
            "Missing class declaration for: \"{0}\"",
            "Code generation",
            DiagnosticSeverity.Error,
            true);
        private static readonly DiagnosticDescriptor _missingInterfaceDeclaration = new("G0008",
            "Missing interface declaration",
            "Missing interface declaration for: \"{0}\"",
            "Code generation",
            DiagnosticSeverity.Error,
            true);
        private const string RELATIVE_README_FILE_PATH = "../../ReadMe.md";
        private static string _readMeFilePath = string.Empty;


        protected override void InitializeCore()
        {
            // https://docs.microsoft.com/en-sg/visualstudio/releases/2019/release-notes-preview#--visual-studio-2019-version-1610-preview-2-
            //Debugger.Launch();
        }

        protected override void ExecuteCore(ref GeneratorExecutionContext context)
        {
            CancellationToken cancellationToken = context.CancellationToken;
            if (cancellationToken.IsCancellationRequested) return;

            // Get syntax receiver
            if (context.SyntaxReceiver is not ApiDocumentationGeneratorSyntaxReceiver apiDocumentationGeneratorSyntaxReceiver)
            {
                return;
            }

            // Parse readme
            if (_readMeFilePath == string.Empty)
            {
                _readMeFilePath = Path.Combine(_projectDirectory, RELATIVE_README_FILE_PATH);
            }

            string readMeContents;
            lock (this)
            {
                if (!File.Exists(_readMeFilePath))
                {
                    return; // Project has no readme
                }

                readMeContents = File.ReadAllText(_readMeFilePath);
            }
            MatchCollection matches = Regex.Matches(readMeContents, @"<!--\s+(.*?)\s+generated\s+docs\s+--(>).*?(<)!--\s+\1\s+generated\s+docs\s+-->", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (matches.Count == 0)
            {
                return; // No types to generate docs for
            }
            List<(int indexBeforeDocs, int indexAfterDocs, string typeName)> generatedDocsTypes = new();
            foreach (Match match in matches)
            {
                generatedDocsTypes.Add((match.Groups[2].Index, match.Groups[3].Index, match.Groups[1].Value));
            }

            // Generate docs
            StringBuilder stringBuilder = new();
            int nextStartIndex = 0;
            foreach ((int indexBeforeDocs, int indexAfterDocs, string typeName) in generatedDocsTypes)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                stringBuilder.
                    Append(readMeContents, nextStartIndex, indexBeforeDocs - nextStartIndex + 1).
                    Append("\n\n");

                if (typeName.StartsWith("I"))
                {
                    if (!apiDocumentationGeneratorSyntaxReceiver.PublicInterfaceDeclarations.TryGetValue(typeName, out InterfaceDeclarationSyntax interfaceDeclarationSyntax))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(_missingInterfaceDeclaration, null, typeName));
                        continue;
                    }

                    stringBuilder.AppendInterfaceDocumentation(interfaceDeclarationSyntax);
                }
                else
                {
                    if (!apiDocumentationGeneratorSyntaxReceiver.PublicClassDeclarations.TryGetValue(typeName, out ClassDeclarationSyntax classDeclarationSyntax))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(_missingClassDeclaration, null, typeName));
                        continue;
                    }

                    stringBuilder.AppendClassDocumentation(classDeclarationSyntax, ref context);
                }

                nextStartIndex = indexAfterDocs;
            }
            stringBuilder.Append(readMeContents, nextStartIndex, readMeContents.Length - nextStartIndex);

            // Update file
            string newReadMeContents = stringBuilder.ToString();
            if (cancellationToken.IsCancellationRequested || newReadMeContents == readMeContents)
            {
                return;
            }
            lock (this)
            {
                File.WriteAllText(_readMeFilePath, newReadMeContents);
            }
        }
    }

    public static class StringBuilderExtensions
    {
        public static StringBuilder AppendInterfaceDocumentation(this StringBuilder stringBuilder, InterfaceDeclarationSyntax interfaceDeclarationSyntax)
        {
            // TODO add logic when we have a project where we need to generate docs for interfaces
            return stringBuilder;
        }

        public static StringBuilder AppendClassDocumentation(this StringBuilder stringBuilder, ClassDeclarationSyntax classDeclarationSyntax, ref GeneratorExecutionContext context)
        {
            Compilation compilation = context.Compilation;
            SemanticModel semanticModel = compilation.GetSemanticModel(classDeclarationSyntax.SyntaxTree);

            // Class title
            INamedTypeSymbol? classSymbol = semanticModel.GetDeclaredSymbol(classDeclarationSyntax);
            if (classSymbol == null)
            {
                return stringBuilder;
            }
            stringBuilder.
                Append("### ").
                Append(classSymbol.ToDisplayString(DisplayFormats.TypeTitleDisplayFormat)).
                AppendLine(" Class");

            // Members
            ImmutableArray<ISymbol> memberSymbols = classSymbol.GetMembers();
            if (memberSymbols.Count() == 0)
            {
                return stringBuilder;
            }
            IEnumerable<IMethodSymbol> methodSymbols = memberSymbols.OfType<IMethodSymbol>();

            // Members - Constructors
            IEnumerable<IMethodSymbol> constructorSymbols = methodSymbols.Where(methodSymbol => methodSymbol.MethodKind == MethodKind.Constructor &&
                methodSymbol.DeclaredAccessibility == Accessibility.Public);
            if (constructorSymbols.Count() > 0)
            {
                stringBuilder.AppendLine(@"#### Constructors");

                foreach (IMethodSymbol constructorSymbol in constructorSymbols)
                {
                    XElement? rootXmlElement = TryGetXmlDocumentationRootElement(constructorSymbol);

                    stringBuilder.
                        AppendMemberTitle(constructorSymbol, DisplayFormats.ConstructorTitleDisplayFormat).
                        AppendSummary(rootXmlElement, compilation).
                        AppendSignature(constructorSymbol).
                        AppendParameters(constructorSymbol, rootXmlElement, compilation).
                        AppendRemarks(rootXmlElement, compilation);
                }
            }

            // Members - Properties
            IEnumerable<IPropertySymbol> propertySymbols = memberSymbols.OfType<IPropertySymbol>();
            if (propertySymbols.Count() > 0)
            {
                stringBuilder.AppendLine(@"#### Properties");

                foreach (IPropertySymbol propertySymbol in propertySymbols)
                {
                    XElement? rootXmlElement = TryGetXmlDocumentationRootElement(propertySymbol);

                    stringBuilder.
                        AppendMemberTitle(propertySymbol, DisplayFormats.propertyTitleDisplayFormat).
                        AppendSummary(rootXmlElement, compilation).
                        AppendSignature(propertySymbol).
                        AppendRemarks(rootXmlElement, compilation);
                }
            }

            // Members - Ordinary methods
            IEnumerable<IMethodSymbol> ordinaryMethodSymbols = methodSymbols.Where(methodSymbol => methodSymbol.MethodKind == MethodKind.Ordinary &&
                methodSymbol.DeclaredAccessibility == Accessibility.Public);
            if (ordinaryMethodSymbols.Count() > 0)
            {
                stringBuilder.AppendLine(@"#### Methods");

                foreach (IMethodSymbol ordinaryMethodSymbol in ordinaryMethodSymbols)
                {
                    XElement? rootXmlElement = TryGetXmlDocumentationRootElement(ordinaryMethodSymbol);

                    stringBuilder.
                        AppendMemberTitle(ordinaryMethodSymbol, DisplayFormats.ordinaryMethodTitleDisplayFormat).
                        AppendSummary(rootXmlElement, compilation).
                        AppendSignature(ordinaryMethodSymbol).
                        AppendParameters(ordinaryMethodSymbol, rootXmlElement, compilation).
                        AppendReturns(rootXmlElement, compilation).
                        AppendExceptions(rootXmlElement, compilation).
                        AppendRemarks(rootXmlElement, compilation);
                }
            }

            return stringBuilder;
        }

        public static StringBuilder AppendMemberTitle(this StringBuilder stringBuilder, ISymbol symbol, SymbolDisplayFormat displayFormat)
        {
            return stringBuilder.
                Append("##### ").
                AppendLine(symbol.ToDisplayString(displayFormat));
        }

        public static StringBuilder AppendSummary(this StringBuilder stringBuilder, XElement? rootXmlElement, Compilation compilation)
        {
            if (rootXmlElement == null)
            {
                return stringBuilder;
            }

            return stringBuilder.AppendXmlDocumentation(rootXmlElement.Element("summary"), compilation);
        }

        public static StringBuilder AppendExceptions(this StringBuilder stringBuilder, XElement? rootXmlElement, Compilation compilation)
        {
            if (rootXmlElement == null)
            {
                return stringBuilder;
            }

            IEnumerable<XElement> exceptionXmlElements = rootXmlElement.Elements("exception");

            if (exceptionXmlElements.Count() == 0)
            {
                return stringBuilder;
            }
            stringBuilder.AppendLine("###### Exceptions");

            foreach (XElement exceptionXmlElement in exceptionXmlElements)
            {
                string? crefValue = exceptionXmlElement.Attribute("cref")?.Value;
                if (crefValue == null)
                {
                    continue;
                }

                int indexOfLastSeparator = crefValue.LastIndexOf('.');
                string exceptionName = crefValue.Substring(indexOfLastSeparator + 1);

                stringBuilder.
                    Append('`').
                    Append(exceptionName).
                    AppendLine("`  ").
                    AppendXmlDocumentation(exceptionXmlElement, compilation);
            }

            return stringBuilder;
        }

        public static StringBuilder AppendReturns(this StringBuilder stringBuilder, XElement? rootXmlElement, Compilation compilation)
        {
            if (rootXmlElement == null)
            {
                return stringBuilder;
            }

            XElement returnsXmlElement = rootXmlElement.Element("returns");

            if (returnsXmlElement == null)
            {
                return stringBuilder;
            }

            return stringBuilder.
                AppendLine("###### Returns").
                AppendXmlDocumentation(returnsXmlElement, compilation);
        }

        public static StringBuilder AppendRemarks(this StringBuilder stringBuilder, XElement? rootXmlElement, Compilation compilation)
        {
            if (rootXmlElement == null)
            {
                return stringBuilder;
            }

            XElement remarksXmlElement = rootXmlElement.Element("remarks");

            if (remarksXmlElement == null)
            {
                return stringBuilder;
            }

            return stringBuilder.
                AppendLine("###### Remarks").
                AppendXmlDocumentation(remarksXmlElement, compilation);
        }

        public static StringBuilder AppendParameters(this StringBuilder stringBuilder, IMethodSymbol methodSymbol, XElement? rootXmlElement, Compilation compilation)
        {
            ImmutableArray<IParameterSymbol> parameters = methodSymbol.Parameters;
            if (parameters.Length == 0)
            {
                return stringBuilder;
            }

            IEnumerable<XElement>? paramXmlElements = null;
            if (rootXmlElement != null)
            {
                paramXmlElements = rootXmlElement.Elements("param");
            }

            stringBuilder.AppendLine("###### Parameters");
            foreach (IParameterSymbol parameterSymbol in parameters)
            {
                string parameterName = parameterSymbol.Name;

                stringBuilder.
                    Append(parameterName).
                    Append(" `").
                    Append(parameterSymbol.Type.ToDisplayString(DisplayFormats.TypeInlineDisplayFormat)).
                    AppendLine("`  ");

                if (paramXmlElements != null)
                {
                    XElement? paramXmlElement = paramXmlElements.FirstOrDefault(paramXmlElement => paramXmlElement.Attribute("name").Value == parameterName);

                    if (paramXmlElement != null)
                    {
                        stringBuilder.AppendXmlDocumentation(paramXmlElement, compilation);
                    }
                }

                stringBuilder.Append('\n'); // New paragraph for each parameter
            }

            stringBuilder.Length -= 1; // Remove last \n

            return stringBuilder;
        }

        public static StringBuilder AppendSignature(this StringBuilder stringBuilder, ISymbol symbol)
        {
            return stringBuilder.
                AppendLine("```csharp").
                AppendLine(symbol.ToDisplayString(DisplayFormats.SignatureDisplayFormat)).
                AppendLine("```");
        }

        public static StringBuilder AppendXmlDocumentation(this StringBuilder stringBuilder, XNode xNode, Compilation compilation)
        {
            if (xNode == null)
            {
                return stringBuilder;
            }

            stringBuilder.AppendXmlNodeContents(xNode, compilation);
            stringBuilder.TrimEnd();
            stringBuilder.AppendLine();

            return stringBuilder;
        }

        public static void AppendXmlNodeContents(this StringBuilder stringBuilder, XNode xNode, Compilation compilation)
        {
            if (xNode.NodeType == XmlNodeType.Text)
            {
                string nodeText = xNode.ToString();
                stringBuilder.Append(nodeText);

                return;
            }

            if (xNode.NodeType != XmlNodeType.Element)
            {
                return;
            }

            var xElement = (XElement)xNode;
            XName elementName = xElement.Name;

            if (elementName == "see")
            {
                string? crefValue = xElement.Attribute("cref")?.Value;

                if (crefValue == null)
                {
                    return;
                }

                ISymbol? seeSymbol = null;
                SymbolDisplayFormat? displayFormat = null;
                if (crefValue.StartsWith("T:"))
                {
                    seeSymbol = compilation.GetTypeByMetadataName(crefValue.Substring(2)); // Drop "T:" prefix
                    displayFormat = DisplayFormats.TypeInlineDisplayFormat;
                }
                else if (crefValue.StartsWith("M:") || crefValue.StartsWith("P:") || crefValue.StartsWith("F:")) // Method, field or property
                {
                    int indexOfLastSeparator = crefValue.LastIndexOf('.');
                    string typeFullyQualifiedName = crefValue.Substring(2, indexOfLastSeparator - 2);
                    INamedTypeSymbol? typeSymbol = compilation.GetTypeByMetadataName(typeFullyQualifiedName);

                    if (typeSymbol == null)
                    {
                        return;
                    }

                    string methodName = crefValue.Substring(indexOfLastSeparator + 1);
                    seeSymbol = typeSymbol.GetMembers(methodName).FirstOrDefault(); // We can't know which overload, so just take first
                    displayFormat = DisplayFormats.MethodInlineDisplayFormat;
                }

                if (seeSymbol == null)
                {
                    return;
                }

                stringBuilder.
                    Append('`').
                    Append(seeSymbol.ToDisplayString(displayFormat)).
                    Append('`');

                return;
            }

            if (elementName == "c")
            {
                stringBuilder.Append('`');
            }
            else if (elementName == "a")
            {
                stringBuilder.Append('[');
            }

            // Iterate over child nodes
            foreach (XNode descendantXNode in xElement.Nodes())
            {
                stringBuilder.AppendXmlNodeContents(descendantXNode, compilation);
            }

            if (elementName == "c")
            {
                stringBuilder.Append('`');
            }
            else if (elementName == "a")
            {
                stringBuilder.
                    Append("](").
                    Append(xElement.Attribute("href")?.Value ?? string.Empty).
                    Append(')');
            }
            else if (elementName == "para")
            {
                stringBuilder.Append("  \n\n");
            }
        }

        // https://stackoverflow.com/questions/24769701/trim-whitespace-from-the-end-of-a-stringbuilder-without-calling-tostring-trim
        public static StringBuilder TrimEnd(this StringBuilder stringBuilder)
        {
            if (stringBuilder.Length == 0) return stringBuilder;

            int i = stringBuilder.Length - 1;

            for (; i >= 0; i--)
                if (!char.IsWhiteSpace(stringBuilder[i]))
                    break;

            if (i < stringBuilder.Length - 1)
                stringBuilder.Length = i + 1;

            return stringBuilder;
        }


        private static XElement? TryGetXmlDocumentationRootElement(ISymbol symbol)
        {
            string? xmlComment = symbol.GetDocumentationCommentXml(); // Note: this method indents our XML, can mess up text indentation

            if (string.IsNullOrWhiteSpace(xmlComment))
            {
                return null;
            }

            xmlComment = Regex.Replace(xmlComment, "^    ", "", RegexOptions.Multiline); // Get rid of indents

            XElement rootElement;
            try
            {
                rootElement = XDocument.Parse(xmlComment).Root;
            }
            catch
            {
                // Do nothing if xml is malformed
                return null;
            }

            XElement inheritDocElement = rootElement.Element("inheritdoc");

            if (inheritDocElement == null)
            {
                return rootElement;
            }

            ImmutableArray<INamedTypeSymbol> containingTypeInterfaceSymbols = symbol.ContainingType.AllInterfaces;
            foreach (INamedTypeSymbol containingTypeInterfaceSymbol in containingTypeInterfaceSymbols)
            {
                ImmutableArray<ISymbol> memberSymbols = containingTypeInterfaceSymbol.GetMembers();
                foreach (ISymbol memberSymbol in memberSymbols)
                {
                    // TODO
                    // - More stringent checks to determine whether symbol is the implementation of memberSymbol,
                    //   in particular, methods could be overloaded. Add checks when we have code to test it on.
                    if (symbol.Kind != memberSymbol.Kind ||
                        symbol.Name != memberSymbol.Name)
                    {
                        continue;
                    }

                    return TryGetXmlDocumentationRootElement(memberSymbol);
                }
            }

            return null;
        }
    }

    public static class DisplayFormats
    {
        public static readonly SymbolDisplayFormat TypeTitleDisplayFormat = new(genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);
        public static readonly SymbolDisplayFormat ConstructorTitleDisplayFormat = new(
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions: SymbolDisplayMemberOptions.IncludeParameters,
            parameterOptions: SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeType,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.UseErrorTypeSymbolName);
        public static readonly SymbolDisplayFormat ordinaryMethodTitleDisplayFormat = new(genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions: SymbolDisplayMemberOptions.IncludeExplicitInterface | SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType,
            parameterOptions: SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeType,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.UseErrorTypeSymbolName);
        public static readonly SymbolDisplayFormat propertyTitleDisplayFormat = ordinaryMethodTitleDisplayFormat;
        public static readonly SymbolDisplayFormat SignatureDisplayFormat = new(genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility | SymbolDisplayMemberOptions.IncludeModifiers | SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeType,
            parameterOptions: SymbolDisplayParameterOptions.IncludeParamsRefOut | SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName | SymbolDisplayParameterOptions.IncludeDefaultValue | SymbolDisplayParameterOptions.IncludeExtensionThis | SymbolDisplayParameterOptions.IncludeOptionalBrackets,
            propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.UseErrorTypeSymbolName | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        public static readonly SymbolDisplayFormat TypeInlineDisplayFormat = new(genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.UseErrorTypeSymbolName);
        public static readonly SymbolDisplayFormat MethodInlineDisplayFormat = new(genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions: SymbolDisplayMemberOptions.IncludeContainingType,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.UseErrorTypeSymbolName);
    }

    public class ApiDocumentationGeneratorSyntaxReceiver : ISyntaxReceiver
    {
        public Dictionary<string, ClassDeclarationSyntax> PublicClassDeclarations = new();
        public Dictionary<string, InterfaceDeclarationSyntax> PublicInterfaceDeclarations = new();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is ClassDeclarationSyntax classDeclarationSyntax)
            {
                if (classDeclarationSyntax.Modifiers.Any(SyntaxKind.PublicKeyword))
                {
                    PublicClassDeclarations.Add(classDeclarationSyntax.Identifier.ValueText, classDeclarationSyntax);
                }

                return;
            }

            if (syntaxNode is InterfaceDeclarationSyntax interfaceDeclarationSyntax &&
                interfaceDeclarationSyntax.Modifiers.Any(SyntaxKind.PublicKeyword))
            {
                PublicInterfaceDeclarations.Add(interfaceDeclarationSyntax.Identifier.ValueText, interfaceDeclarationSyntax);
            }
        }
    }
}
