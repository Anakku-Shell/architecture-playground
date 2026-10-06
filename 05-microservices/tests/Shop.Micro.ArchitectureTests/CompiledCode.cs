using Mono.Cecil;
using Mono.Cecil.Cil;
using Assembly = System.Reflection.Assembly;

namespace Shop.Micro.ArchitectureTests;

/// <summary>Everything one source type uses in its compiled code, including inside its lambdas and async methods.</summary>
/// <param name="Type">Full name of the type as written in source (e.g. <c>Shop.Micro.Catalog.Api.Products.ProductEndpoints</c>).</param>
/// <param name="Namespace">Its namespace.</param>
/// <param name="UsedTypes">Full names of every type it references (namespace + name, generic arguments included).</param>
/// <param name="CalledMethods">Methods it calls, as <c>DeclaringTypeFullName::MethodName</c>.</param>
internal sealed record TypeUsage(string Type, string Namespace, IReadOnlySet<string> UsedTypes, IReadOnlySet<string> CalledMethods)
{
    public bool InNamespace(string ns) => Namespace == ns || Namespace.StartsWith(ns + ".", StringComparison.Ordinal);
}

/// <summary>
/// Reads the compiled IL of an assembly with Mono.Cecil and attributes every reference to the type that wrote
/// it in source. Needed because the C# compiler moves lambda bodies and async methods into generated classes
/// (<c>&lt;&gt;c__DisplayClass…</c>, <c>&lt;HandleAsync&gt;d__…</c>), and ArchUnitNET does not attribute what happens
/// inside an async lambda back to its declaring type: a rule built on it would pass whatever those lambdas
/// do. Here every nested, compiler-generated type counts as part of its outermost declaring type.
/// Guide: §6.6.
/// </summary>
internal static class CompiledCode
{
    public static IReadOnlyList<TypeUsage> Read(Assembly assembly)
    {
        using var module = ModuleDefinition.ReadModule(assembly.Location);
        return [.. module.Types
            .Where(t => t.Name != "<Module>" && !t.Name.StartsWith('<'))
            .Select(Collect)];
    }

    private static TypeUsage Collect(TypeDefinition type)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        var methods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in SelfAndNested(type))
        {
            AddType(part.BaseType, types);
            foreach (var implemented in part.Interfaces)
            {
                AddType(implemented.InterfaceType, types);
            }

            foreach (var field in part.Fields)
            {
                AddType(field.FieldType, types);
            }

            foreach (var method in part.Methods)
            {
                AddType(method.ReturnType, types);
                foreach (var parameter in method.Parameters)
                {
                    AddType(parameter.ParameterType, types);
                }

                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var variable in method.Body.Variables)
                {
                    AddType(variable.VariableType, types);
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    AddOperand(instruction, types, methods);
                }
            }
        }

        return new TypeUsage(type.FullName, type.Namespace, types, methods);
    }

    private static IEnumerable<TypeDefinition> SelfAndNested(TypeDefinition type) =>
        type.NestedTypes.SelectMany(SelfAndNested).Prepend(type);

    private static void AddOperand(Instruction instruction, HashSet<string> types, HashSet<string> methods)
    {
        switch (instruction.Operand)
        {
            case MethodReference method:
                AddType(method.DeclaringType, types);
                AddType(method.ReturnType, types);
                methods.Add($"{Outermost(method.DeclaringType).FullName}::{method.Name}");
                if (method is GenericInstanceMethod generic)
                {
                    foreach (var argument in generic.GenericArguments)
                    {
                        AddType(argument, types);
                    }
                }

                break;
            case FieldReference field:
                AddType(field.DeclaringType, types);
                AddType(field.FieldType, types);
                break;
            case TypeReference typeReference:
                AddType(typeReference, types);
                break;
        }
    }

    private static void AddType(TypeReference? type, HashSet<string> types)
    {
        switch (type)
        {
            case null or GenericParameter:
                return;
            case GenericInstanceType generic:
                AddType(generic.ElementType, types);
                foreach (var argument in generic.GenericArguments)
                {
                    AddType(argument, types);
                }

                return;
            case TypeSpecification specification:
                AddType(specification.ElementType, types);
                return;
            default:
                types.Add(Outermost(type).FullName);
                return;
        }
    }

    private static TypeReference Outermost(TypeReference type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type;
    }
}
