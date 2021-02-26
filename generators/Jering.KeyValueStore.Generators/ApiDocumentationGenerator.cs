using Microsoft.CodeAnalysis;
using System;

#nullable enable

namespace Jering.KeyValueStore.Generators
{
    // TODO
    // - Locate readme file
    // - collect public class, public method declaration syntax nodes
    // - collect public class public property declaration syntax nodes
    // - organize public member declarations by class
    // - we should get two lists, one for MixedStorageKVStore and one for MixedStorageKVStoreOptions
    // - generate documentation for non-options types first
    //   - iterate over members
    //   - if xml comment is inheritdoc, use symbols to find base method, get actual xml comments
    //   - generate markdown
    // - repeat for options
    [Generator]
    public class ApiDocumentationGenerator : SourceGenerator
    {
        protected override void ExecuteCore(ref GeneratorExecutionContext context)
        {
            throw new NotImplementedException();
        }

        protected override void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            throw new NotImplementedException();
        }
    }
}
