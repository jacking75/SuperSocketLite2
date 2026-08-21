using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SuperSocketLite.Analyzers;

/// <summary>
/// 풀에서 빌린 버퍼를 zero-copy <c>Send</c> 로 보내는 것을 잡는다 (SSL003).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SendBufferAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.PooledBufferSentWithoutCopy);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (KnownTypes.TryCreate(start.Compilation) is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method)
        {
            return;
        }

        // 복사해 가는 오버로드는 안전하다. 이름이 정확히 Send / TrySend 인 것만 본다.
        if (method.Name is not ("Send" or "TrySend"))
        {
            return;
        }

        if (!KnownTypes.DerivesFrom(method.ContainingType, KnownTypes.AppSessionMetadataName)
            && !KnownTypes.DerivesFrom(method.ContainingType, KnownTypes.AppSessionInterfaceMetadataName))
        {
            return;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (GetRootIdentifier(argument.Expression) is not { } identifier)
            {
                continue;
            }

            if (context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol
                is not ILocalSymbol local)
            {
                continue;
            }

            if (!IsRentedFromArrayPool(local, context))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.PooledBufferSentWithoutCopy,
                argument.GetLocation(),
                local.Name,
                method.Name));

            return;
        }
    }

    /// <summary>
    /// <c>rented</c>, <c>rented.AsSpan(0, n)</c>, <c>new ArraySegment&lt;byte&gt;(rented, 0, n)</c> 처럼
    /// 지역 변수에 뿌리를 둔 식에서 그 지역 변수를 꺼낸다.
    /// </summary>
    private static IdentifierNameSyntax? GetRootIdentifier(ExpressionSyntax expression)
    {
        var current = expression;

        while (true)
        {
            switch (current)
            {
                case IdentifierNameSyntax identifier:
                    return identifier;

                case MemberAccessExpressionSyntax member:
                    current = member.Expression;
                    continue;

                case InvocationExpressionSyntax invocation:
                    current = invocation.Expression;
                    continue;

                case ElementAccessExpressionSyntax elementAccess:
                    current = elementAccess.Expression;
                    continue;

                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized.Expression;
                    continue;

                case ObjectCreationExpressionSyntax creation
                    when creation.ArgumentList?.Arguments.Count > 0:
                    current = creation.ArgumentList.Arguments[0].Expression;
                    continue;

                default:
                    return null;
            }
        }
    }

    /// <summary>지역 변수가 <c>ArrayPool&lt;T&gt;.Shared.Rent(...)</c> 로 초기화되었는가.</summary>
    private static bool IsRentedFromArrayPool(ILocalSymbol local, SyntaxNodeAnalysisContext context)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(context.CancellationToken) is not VariableDeclaratorSyntax declarator)
            {
                continue;
            }

            if (declarator.Initializer?.Value is not InvocationExpressionSyntax initializer)
            {
                continue;
            }

            // 지역 변수의 선언은 언제나 사용처와 같은 트리에 있다. 아니라면 판단하지 않는다.
            if (initializer.SyntaxTree != context.Node.SyntaxTree)
            {
                continue;
            }

            if (context.SemanticModel.GetSymbolInfo(initializer, context.CancellationToken).Symbol
                is not IMethodSymbol rent)
            {
                continue;
            }

            if (rent.Name == "Rent"
                && KnownTypes.DerivesFrom(rent.ContainingType, KnownTypes.ArrayPoolMetadataName))
            {
                return true;
            }
        }

        return false;
    }
}
