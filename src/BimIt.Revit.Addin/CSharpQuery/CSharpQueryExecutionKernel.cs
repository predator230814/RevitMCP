using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using BimIt.Revit.Compiler;

namespace BimIt.Revit.Addin.CSharpQuery;

internal readonly record struct CSharpQueryExecutionEvidence(
    CSharpQueryExecution Result,
    WeakReference? LoadContext);

internal static class CSharpQueryExecutionKernel
{
    public static CSharpQueryExecutionEvidence Execute(byte[] assemblyBytes, Document? document, Func<Element, string> elementRef)
    {
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        ArgumentNullException.ThrowIfNull(elementRef);
        return ExecuteCollectible(assemblyBytes, document, elementRef);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CSharpQueryExecutionEvidence ExecuteCollectible(
        byte[] assemblyBytes,
        Document? document,
        Func<Element, string> elementRef)
    {
        var context = new GeneratedQueryLoadContext();
        var reference = new WeakReference(context, trackResurrection: false);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var copied = Invoke(context, assemblyBytes, document, elementRef);
            return new CSharpQueryExecutionEvidence(
                CSharpQueryExecution.Succeeded(copied, Elapsed(started)),
                reference);
        }
        catch (CSharpQueryBudgetExceededException)
        {
            return Failed(CSharpQueryCodes.BudgetExceeded, null, started, reference);
        }
        catch (CSharpQueryCopyException ex)
        {
            return Failed(ex.Code, null, started, reference);
        }
        catch
        {
            return Failed(CSharpQueryCodes.ExecutionFailed, null, started, reference);
        }
        finally
        {
            context.Unload();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CSharpQueryCopiedResult Invoke(
        GeneratedQueryLoadContext context,
        byte[] assemblyBytes,
        Document? document,
        Func<Element, string> elementRef)
    {
        Assembly assembly;
        MethodInfo method;
        try
        {
            assembly = context.LoadGenerated(assemblyBytes);
            method = ResolveEntry(assembly);
        }
        catch (CSharpQueryShapeException)
        {
            throw;
        }
        catch
        {
            throw new CSharpQueryShapeException();
        }

        var budget = new CSharpQueryBudget(CSharpQueryLimits.LoopCharges);
        object? value;
        try
        {
            value = method.Invoke(null, [document, elementRef, (Action)budget.Charge]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is CSharpQueryBudgetExceededException)
        {
            throw new CSharpQueryBudgetExceededException();
        }
        catch (TargetInvocationException)
        {
            throw new CSharpQueryShapeException();
        }

        return CSharpQueryResultCopier.Copy(value, assembly);
    }

    private static MethodInfo ResolveEntry(Assembly assembly)
    {
        var type = assembly.GetType(
            CompilerProtocol.GeneratedNamespace + "." + CompilerProtocol.GeneratedTypeName,
            throwOnError: false);
        if (type is null || !type.IsPublic)
        {
            throw new CSharpQueryShapeException();
        }

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (methods.Length != 1)
        {
            throw new CSharpQueryShapeException();
        }

        var method = methods[0];
        var parameters = method.GetParameters();
        if (method.Name != CompilerProtocol.GeneratedMethodName
            || method.ReturnType != typeof(object)
            || parameters.Length != 3
            || parameters[0].ParameterType != typeof(Document)
            || parameters[1].ParameterType != typeof(Func<Element, string>)
            || parameters[2].ParameterType != typeof(Action))
        {
            throw new CSharpQueryShapeException();
        }

        return method;
    }

    private static CSharpQueryExecutionEvidence Failed(string code, string? reason, long started, WeakReference reference)
    {
        return new CSharpQueryExecutionEvidence(
            CSharpQueryExecution.Failed(code, reason, Elapsed(started)),
            reference);
    }

    private static int Elapsed(long started)
    {
        var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (milliseconds <= 0)
        {
            return 0;
        }

        return milliseconds >= int.MaxValue ? int.MaxValue : (int)milliseconds;
    }

    private sealed class CSharpQueryShapeException : Exception;
}
