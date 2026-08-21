using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SuperSocketLite.Analyzers;

/// <summary>
/// 요청 핸들러 밖으로 <c>IRequestInfo</c> 와 그 본문이 새어 나가는 것을 잡는다 (SSL001 · SSL002 · SSL005).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequestInfoLifetimeAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            Descriptors.RequestInfoStored,
            Descriptors.RequestInfoCaptured,
            Descriptors.AsyncRequestHandler);

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
                nodeContext => AnalyzeMethod(nodeContext, knownTypes),
                SyntaxKind.MethodDeclaration);
        });
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context, KnownTypes knownTypes)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } method)
        {
            return;
        }

        var requestParameters = method.Parameters
            .Where(p => KnownTypes.Implements(p.Type, knownTypes.RequestInfo))
            .ToImmutableArray();

        if (requestParameters.IsEmpty)
        {
            return;
        }

        // SSL005 — async 핸들러는 첫 await 에서 리턴하므로 그 뒤의 본문 접근이 전부 무효다.
        if (method.IsAsync)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.AsyncRequestHandler,
                declaration.Identifier.GetLocation(),
                method.Name));
        }

        if (declaration.Body is null && declaration.ExpressionBody is null)
        {
            return;
        }

        SyntaxNode body = (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody!;

        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var symbol = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol;

            if (symbol is not IParameterSymbol parameter
                || !requestParameters.Any(p => SymbolEqualityComparer.Default.Equals(p, parameter)))
            {
                continue;
            }

            if (TryReportCapture(context, identifier, body, parameter))
            {
                continue;
            }

            TryReportStore(context, identifier, parameter, knownTypes);
        }
    }

    /// <summary>SSL002 — 람다나 지역 함수 안에서 요청을 참조하면 나중에 실행될 때 이미 무효다.</summary>
    private static bool TryReportCapture(
        SyntaxNodeAnalysisContext context,
        IdentifierNameSyntax identifier,
        SyntaxNode methodBody,
        IParameterSymbol parameter)
    {
        for (var node = identifier.Parent; node is not null && node != methodBody; node = node.Parent)
        {
            if (node is SimpleLambdaExpressionSyntax
                or ParenthesizedLambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax
                or LocalFunctionStatementSyntax)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.RequestInfoCaptured,
                    identifier.GetLocation(),
                    parameter.Name));

                return true;
            }
        }

        return false;
    }

    /// <summary>SSL001 — 요청이나 그 본문을 필드/프로퍼티에 대입하면 다음 패킷이 그 자리를 덮는다.</summary>
    private static void TryReportStore(
        SyntaxNodeAnalysisContext context,
        IdentifierNameSyntax identifier,
        IParameterSymbol parameter,
        KnownTypes knownTypes)
    {
        // request / request.Body / request.Body.Something 처럼 이 식별자에 뿌리를 둔 가장 바깥 식을 찾는다.
        ExpressionSyntax expression = identifier;

        while (expression.Parent is MemberAccessExpressionSyntax member && member.Expression == expression)
        {
            expression = member;
        }

        if (expression.Parent is not AssignmentExpressionSyntax assignment || assignment.Right != expression)
        {
            return;
        }

        // 값만 복사되는 멤버(request.PacketId 등)는 저장해도 안전하다.
        var assignedType = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;

        if (!knownTypes.IsLifetimeBoundType(assignedType))
        {
            return;
        }

        var target = context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol;

        if (target is not IFieldSymbol and not IPropertySymbol)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.RequestInfoStored,
            assignment.GetLocation(),
            expression.ToString()));
    }
}
