using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace VarReplacer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class VarReplacerAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "VarIsBad";
        public const string RealNameProp = "RealName";
        private const string Title = "Var replacer";
        private const string MessageFormat = "Use '{0}' instead of var";
        private const string Description = "Using var will hide the type from code reviewers, so it's best to use the actual type name.";
        private const string Category = "Style";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            VarReplacerAnalyzer.DiagnosticId,
            VarReplacerAnalyzer.Title,
            VarReplacerAnalyzer.MessageFormat,
            VarReplacerAnalyzer.Category,
            DiagnosticSeverity.Hidden,
            isEnabledByDefault: true,
            description: VarReplacerAnalyzer.Description);

        public VarReplacerAnalyzer()
        {
        }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(VarReplacerAnalyzer.Rule); }
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(VarReplacerAnalyzer.AnalyzeDeclaration, SyntaxKind.VariableDeclaration);
            context.RegisterSyntaxNodeAction(VarReplacerAnalyzer.AnalyzeForEach, SyntaxKind.ForEachStatement);
        }

        private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
        {
            VariableDeclarationSyntax node = context.Node as VariableDeclarationSyntax;

            if (node != null &&
                node.Type != null &&
                node.Type.IsVar &&
                node.Variables != null &&
                node.Variables.Count == 1 &&
                !context.CancellationToken.IsCancellationRequested)
            {
                ExpressionSyntax expression = node.Variables[0].Initializer?.Value;
                if (expression != null)
                {
                    TypeInfo expressionType = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
                    CheckVar(context, expressionType.Type, node.Type.GetLocation());
                }
            }
        }

        private static void AnalyzeForEach(SyntaxNodeAnalysisContext context)
        {
            ForEachStatementSyntax node = context.Node as ForEachStatementSyntax;

            if (node != null &&
                node.Type != null &&
                node.Type.IsVar &&
                node.Expression != null &&
                !context.CancellationToken.IsCancellationRequested)
            {
                ForEachStatementInfo info = context.SemanticModel.GetForEachStatementInfo(node);
                CheckVar(context, info.ElementType, node.Type.GetLocation());
            }
        }

        private static void CheckVar(SyntaxNodeAnalysisContext context, ITypeSymbol realType, Location varLocation)
        {
            if (realType != null && !HasAnonymousType(realType) && !context.CancellationToken.IsCancellationRequested)
            {
                string realName = realType.ToMinimalDisplayString(context.SemanticModel, varLocation.SourceSpan.Start);
                IDictionary<string, string> props = new Dictionary<string, string>
                {
                    {  VarReplacerAnalyzer.RealNameProp, realName }
                };

                Diagnostic diagnostic = Diagnostic.Create(
                    VarReplacerAnalyzer.Rule,
                    varLocation,
                    props.ToImmutableDictionary(),
                    realName);

                context.ReportDiagnostic(diagnostic);
            }
        }

        private static bool HasAnonymousType(ITypeSymbol realType)
        {
            if (realType != null)
            {
                if (realType.IsAnonymousType)
                {
                    return true;
                }

                IArrayTypeSymbol arrayType = realType as IArrayTypeSymbol;
                if (arrayType != null && HasAnonymousType(arrayType.ElementType))
                {
                    return true;
                }

                INamedTypeSymbol namedType = realType as INamedTypeSymbol;
                if (namedType != null && namedType.IsGenericType)
                {
                    foreach (ITypeSymbol argument in namedType.TypeArguments)
                    {
                        if (HasAnonymousType(argument as INamedTypeSymbol))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
