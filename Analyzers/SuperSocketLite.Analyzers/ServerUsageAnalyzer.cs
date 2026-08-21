using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SuperSocketLite.Analyzers;

/// <summary>
/// 시퀀스·서버 API 사용에서 흔한 실수를 잡는다 (SSL004 · SSL006 · SSL007).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ServerUsageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            Descriptors.SequenceAssumedSingleSegment,
            Descriptors.SetupResultIgnored,
            Descriptors.SessionEnumerationNotNullChecked);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var knownTypes = KnownTypes.TryCreate(start.Compilation);

            if (knownTypes is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeMemberAccess(nodeContext, knownTypes),
                SyntaxKind.SimpleMemberAccessExpression);

            start.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        });
    }

    /// <summary>SSL004 — <c>sequence.First.Span</c> / <c>sequence.FirstSpan</c> 은 첫 세그먼트만 본다.</summary>
    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context, KnownTypes knownTypes)
    {
        var member = (MemberAccessExpressionSyntax)context.Node;
        var name = member.Name.Identifier.ValueText;

        ExpressionSyntax? sequenceExpression = name switch
        {
            "FirstSpan" => member.Expression,
            "Span" when member.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "First" } inner
                => inner.Expression,
            _ => null,
        };

        if (sequenceExpression is null)
        {
            return;
        }

        var type = context.SemanticModel.GetTypeInfo(sequenceExpression, context.CancellationToken).Type;

        if (type is not INamedTypeSymbol named
            || !SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, knownTypes.ReadOnlySequence))
        {
            return;
        }

        // IsSingleSegment 를 확인하고 들어온 자리는 의도한 최적화다.
        if (IsGuardedBySingleSegmentCheck(member))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.SequenceAssumedSingleSegment,
            member.GetLocation(),
            member.ToString()));
    }

    private static bool IsGuardedBySingleSegmentCheck(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IfStatementSyntax ifStatement
                    when MentionsSingleSegment(ifStatement.Condition):
                    return true;

                case ConditionalExpressionSyntax conditional
                    when MentionsSingleSegment(conditional.Condition):
                    return true;

                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.LogicalAndExpression) && MentionsSingleSegment(binary.Left):
                    return true;

                case MethodDeclarationSyntax or LocalFunctionStatementSyntax:
                    return false;
            }
        }

        return false;
    }

    private static bool MentionsSingleSegment(SyntaxNode condition)
        => condition.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(i => i.Identifier.ValueText == "IsSingleSegment");

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method)
        {
            return;
        }

        if (!KnownTypes.DerivesFrom(method.ContainingType, KnownTypes.AppServerBaseMetadataName))
        {
            return;
        }

        switch (method.Name)
        {
            case "Setup" or "Start"
                when method.ReturnType.SpecialType == SpecialType.System_Boolean
                     && invocation.Parent is ExpressionStatementSyntax:

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.SetupResultIgnored,
                    invocation.GetLocation(),
                    method.Name));
                break;

            case "GetAllSessions" or "GetSessions"
                when IsDereferencedDirectly(invocation):

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.SessionEnumerationNotNullChecked,
                    invocation.GetLocation(),
                    method.Name));
                break;
        }
    }

    /// <summary>
    /// 반환값을 변수에 받지 않고 그 자리에서 바로 쓰고 있는가.
    /// <c>foreach (var s in GetAllSessions())</c> 나 <c>GetAllSessions().Count()</c> 같은 것들이다.
    /// </summary>
    private static bool IsDereferencedDirectly(InvocationExpressionSyntax invocation)
    {
        // 물음표를 붙였으면 null 을 이미 다루고 있는 것이다.
        if (invocation.Parent is ConditionalAccessExpressionSyntax)
        {
            return false;
        }

        return invocation.Parent switch
        {
            ForEachStatementSyntax forEach => forEach.Expression == invocation,
            MemberAccessExpressionSyntax member => member.Expression == invocation,
            _ => false,
        };
    }
}
