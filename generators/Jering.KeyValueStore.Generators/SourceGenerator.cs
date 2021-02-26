using Microsoft.CodeAnalysis;
using System;
using System.IO;
using System.Linq;

namespace Jering.KeyValueStore.Generators
{
    public abstract class SourceGenerator : ISourceGenerator
    {
        protected static readonly DiagnosticDescriptor _unexpectedException = new("G0006",
            "UnexpectedException",
            "UnexpectedException: {0}",
            "Code generation",
            DiagnosticSeverity.Error,
            true);

        private string _logFilePath = string.Empty;

        protected string _projectDirectory;
        protected string _solutionDirectory;

        protected abstract void ExecuteCore(ref GeneratorExecutionContext context);
        protected abstract void OnVisitSyntaxNode(SyntaxNode syntaxNode);

        protected virtual void InitializeCore() { }

        public void Execute(GeneratorExecutionContext context)
        {
            try
            {
                if (_logFilePath == string.Empty)
                {
                    _projectDirectory = Path.GetDirectoryName(context.Compilation.SyntaxTrees.First(tree => tree.FilePath.EndsWith("Program.cs")).FilePath);
                    _solutionDirectory = Path.Combine(_projectDirectory, "../..");
                    _logFilePath = Path.Combine(_projectDirectory, $"{GetType().Name}.txt");
                }

                ExecuteCore(ref context);
            }
            catch (Exception exception)
            {
                context.ReportDiagnostic(Diagnostic.Create(_unexpectedException, null, exception.Message));
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            // Register a factory that can create our custom syntax receiver
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver(OnVisitSyntaxNode));
            InitializeCore();
        }

#pragma warning disable IDE0060 // Unused when logging is off
        protected void LogLine(string message)
#pragma warning restore IDE0060
        {
            //File.AppendAllText(_logFilePath, message + "\n");
        }

        private class SyntaxReceiver : ISyntaxReceiver
        {
            private readonly Action<SyntaxNode> _onVisitSyntaxNode;

            public SyntaxReceiver(Action<SyntaxNode> onVisitSyntaxNode)
            {
                _onVisitSyntaxNode = onVisitSyntaxNode;
            }

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                _onVisitSyntaxNode(syntaxNode);
            }
        }
    }
}
