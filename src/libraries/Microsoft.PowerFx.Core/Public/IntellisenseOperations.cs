﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.Parser;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx.Intellisense
{
    /// <summary>
    /// Various IntelliSense-like operations.
    /// </summary>
    public class IntellisenseOperations
    {
        private readonly CheckResult _checkResult;

        /// <summary>
        /// Initializes a new instance of the <see cref="IntellisenseOperations"/> class.
        /// </summary>
        /// <param name="result">The <see cref="CheckResult"/> to use for IntelliSense operations.</param>
        public IntellisenseOperations(CheckResult result)
        {
            _checkResult = result;
        }

        /// <summary>
        /// Checks whether a call to a function with the specified name is valid with the given argument list. Additionally returns (as an out parameter) the return type of this invocation.
        /// Note: all arguments must belong to the formula that belongs to this <see cref="CheckResult" />.
        /// </summary>
        /// <param name="functionName">The name of the function to validate.</param>
        /// <param name="args">The list of argument nodes for the function call.</param>
        /// <param name="retType">When this method returns, contains the return type of the function invocation if valid; otherwise, null.</param>
        /// <returns>True if the function invocation is valid; otherwise, false.</returns>
        public bool ValidateInvocation(string functionName, IReadOnlyList<TexlNode> args, out FormulaType retType)
        {
            retType = null;

            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            var binding = _checkResult.Binding;

            foreach ((var arg, var index) in args.Select((value, index) => (value, index)))
            {
                if (arg == null)
                {
                    throw new ArgumentNullException(nameof(args), $"Argument {index} is null");
                }

                if (!binding.IsNodeValid(arg))
                {
                    throw new ArgumentException($"Argument {index} does not belong to this result");
                }
            }

            var types = args.Select(node => binding.GetType(node)).ToArray();

            if (!TryParseFunctionNameWithNamespace(functionName, out var fncIdent))
            {
                return false;
            }

            // Note: there could be multiple functions (e.g., overloads) with the same name and arity,
            //  hence loop through candidates and check whether one of them matches.
            var fncs = GetFunctionsByIdentifier(fncIdent).Where(fnc => args.Count >= fnc.MinArity && args.Count <= fnc.MaxArity);

            foreach (var fnc in fncs)
            {
                var result =
                    fnc.HandleCheckInvocation(
                        _checkResult.Binding,
                        args.ToArray(),
                        types,
                        binding.ErrorContainer,
                        out var retDType,
                        out _);

                if (result)
                {
                    retType = FormulaType.Build(retDType);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether the function's n-th argument can be row scoped in any of the overloads of the function.
        /// </summary>
        /// <param name="functionIdentifier">The identifier of the function to check.</param>
        /// <param name="argNum">The zero-based index of the argument to check for row scoping.</param>
        /// <returns>True if the argument can be row scoped in any overload; otherwise, false.</returns>
        public bool MaybeRowScopeArg(Identifier functionIdentifier, int argNum)
        {
            if (functionIdentifier is null)
            {
                throw new ArgumentNullException(nameof(functionIdentifier));
            }

            if (argNum < 0)
            {
                throw new ArgumentException("Expected non-negative value", nameof(argNum));
            }

            var fncs = GetFunctionsByIdentifier(functionIdentifier);

            foreach (var fnc in fncs)
            {
                if (fnc.ScopeInfo == null || fnc.ScopeInfo.AppliesToArgument == null)
                {
                    continue;
                }

                if (fnc.ScopeInfo.AppliesToArgument(argNum))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether the function's n-th argument can be row scoped in any of the overloads of the function.
        /// </summary>
        /// <param name="functionName">The name of the function to check.</param>
        /// <param name="argNum">The zero-based index of the argument to check for row scoping.</param>
        /// <returns>True if the argument can be row scoped in any overload; otherwise, false.</returns>
        public bool MaybeRowScopeArg(string functionName, int argNum)
        {
            if (functionName == null)
            {
                throw new ArgumentNullException(nameof(functionName));
            }

            if (!TryParseFunctionNameWithNamespace(functionName, out Identifier ident))
            {
                return false;
            }

            return MaybeRowScopeArg(ident, argNum);
        }

        // Gets all functions by identifier (possible multiple results due to overloads).
        private IEnumerable<TexlFunction> GetFunctionsByIdentifier(Identifier ident)
        {
            return _checkResult.Binding.NameResolver.Functions.WithName(ident.Name, ident.Namespace);
        }

        // Parse a function name string into an identifier (namespace and name).
        internal static bool TryParseFunctionNameWithNamespace(string functionName, out Identifier ident)
        {
            ident = null;
            var parseResult = TexlParser.ParseScript($"{functionName}()");
            if (!parseResult.IsSuccess)
            {
                return false;
            }

            ident = parseResult.Root.AsCall()?.Head;
            return ident != null;
        }
    }
}
