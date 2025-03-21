﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.PowerFx.Core.Binding;
using Microsoft.PowerFx.Core.IR;
using Microsoft.PowerFx.Core.IR.Nodes;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Core.Logging;
using Microsoft.PowerFx.Core.Public;
using Microsoft.PowerFx.Core.Public.Types.TypeCheckers;
using Microsoft.PowerFx.Core.Texl.Intellisense;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx
{
    /// <summary>
    /// Holds work such as parsing, binding, error checking done on a single expression. 
    /// Different options require different work. 
    /// Tracks which work is done so that it is not double repeated.
    /// </summary>
    public class CheckResult : IOperationStatus
    {
        #region User Inputs
        // Captured from user, set once via a Set*() function. 

        /// <summary>
        /// The source engine this was created against. 
        /// This is critical for calling back to populate the rest of the results. 
        /// </summary>
        private readonly Engine _engine;

        // The raw expression test. 
        private string _expression;

        private ParserOptions _parserOptions;

        private CultureInfo _defaultErrorCulture;

        // We must call all Set() operations before calling Apply(). 
        // This is because Apply() methods can be called in lazy ways, and so we need a gaurantee
        // that the Set() conditions are fixed. 
        private bool _beginApply;

        // Information for binding. 
        private bool _setBindingCalled;
        internal ReadOnlySymbolTable _symbols; // can be null
        private VersionHash _symbolHash; // hash of _symbols at time of assignment

        #endregion

        [Obsolete("Use public constructor")]
        internal CheckResult()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckResult"/> class.
        /// </summary>
        /// <param name="source">Engine used to handle Apply operations.</param>
        public CheckResult(Engine source)
        {
            this._engine = source ?? throw new ArgumentNullException(nameof(source));
        }

        internal Engine Engine => _engine;

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckResult"/> class.
        /// Create a "failed" check around a set of errors. 
        /// Can't do any other operations. 
        /// </summary>
        /// <param name="extraErrors"></param>
        public CheckResult(IEnumerable<ExpressionError> extraErrors)
        {
            this._errors.AddRange(extraErrors);
        }

        #region Set info from User. 
        private void VerifyEngine([CallerMemberName] string memberName = "")
        {
            if (_engine == null)
            {
                throw new InvalidOperationException($"Can't call {memberName} without an engine.");
            }

            // Can't call Set*() methods after the init phase.
            if (_beginApply)
            {
                throw new InvalidOperationException($"Can't call {memberName} after calling an Apply*() method.");
            }
        }

        public CheckResult SetText(ParseResult parse)
        {
            VerifyEngine();

            if (parse == null)
            {
                throw new ArgumentNullException(nameof(parse));
            }

            if (_expression != null)
            {
                throw new InvalidOperationException($"Can only call {nameof(SetText)} once.");
            }

            _expression = parse.Text;
            _parserOptions = parse.Options;
            this.Parse = parse;

            return this;
        }

        public CheckResult SetText(string expression, ParserOptions parserOptions = null)
        {
            VerifyEngine();

            expression = expression ?? throw new ArgumentNullException(nameof(expression));

            if (_expression != null)
            {
                throw new InvalidOperationException($"Can only call {nameof(SetText)} once.");
            }

            _expression = expression;
            _parserOptions = parserOptions ?? Engine.GetDefaultParserOptionsCopy();
            ParserCultureInfo = _parserOptions.Culture;

            return this;
        }

        // Set the default culture for localizing error messages and intellisense suggestions. 
        public CheckResult SetDefaultErrorCulture(CultureInfo culture)
        {
            VerifyEngine();

            this._defaultErrorCulture = culture;
            return this;
        }

        // Symbols could be null if no additional symbols are provided. 
        public CheckResult SetBindingInfo(ReadOnlySymbolTable symbols)
        {
            VerifyEngine();

            if (_setBindingCalled)
            {
                throw new InvalidOperationException($"Can only call {nameof(SetBindingInfo)} once.");
            }

            _symbols = symbols;

            if (_symbols != null)
            {
                _symbolHash = _symbols.VersionHash;
            }

            _setBindingCalled = true;
            return this;
        }

        public CheckResult SetBindingInfo(RecordType parameterType)
        {
            ReadOnlySymbolTable symbolTable = null;
            if (parameterType != null)
            {
                symbolTable = SymbolTable.NewFromRecord(parameterType);
            }

            return this.SetBindingInfo(symbolTable);
        }

        private FormulaType _expectedReturnTypes;
        private bool _expectedReturnTypesCoerces;

        internal FormulaType ExpectedReturnType => _expectedReturnTypes ?? FormulaType.Unknown;

        // This function only injects errors but does not do any coercion.
        public CheckResult SetExpectedReturnValue(FormulaType type, bool allowCoerceTo = false)
        {
            VerifyEngine();

            _expectedReturnTypes = type;
            _expectedReturnTypesCoerces = allowCoerceTo;
            return this;
        }

        [Obsolete("Use SetExpectedReturnValue(FormulaType type, bool allowCoerceTo) instead")]
        public CheckResult SetExpectedReturnValue(params FormulaType[] expectedReturnTypes)
        {
            VerifyEngine();

            return this.SetExpectedReturnValue(expectedReturnTypes.FirstOrDefault(), true);
        }

        // No additional binding is required
        public CheckResult SetBindingInfo()
        {
            return SetBindingInfo((RecordType)null);
        }
        #endregion

        #region Results from Parsing 

        /// <summary>
        /// Results from parsing. <see cref="ApplyParse"/>.
        /// </summary>
        public ParseResult Parse { get; private set; }
        #endregion

        #region Results from Binding

        /// <summary>
        /// Binding, computed from <see cref="ApplyBindingInternal"/>.
        /// </summary>        
        internal TexlBinding _binding;

        /// <summary> 
        /// Return type of the expression. Null if type can't be determined. 
        /// </summary>
        public FormulaType ReturnType { get; set; }

        #endregion

        #region Results from Dependencies

        private HashSet<string> _topLevelIdentifiers;

        /// <summary>
        /// Names of fields that this formula uses. 
        /// null if unavailable.  
        /// This is only valid when <see cref="IsSuccess"/> is true.
        /// </summary>
        public HashSet<string> TopLevelIdentifiers
        {
            get
            {
                if (_topLevelIdentifiers == null)
                {
                    throw new InvalidOperationException($"Call {nameof(ApplyDependencyAnalysis)} first.");
                }

                return _topLevelIdentifiers;
            }
        }

        #endregion 

        #region Results from Errors

        // All errors accumulated. 
        private readonly List<ExpressionError> _errors = new List<ExpressionError>();
        #endregion

        #region Results from IR 
        private IRResult _irresult;

        #endregion 

        internal TexlBinding Binding
        {
            get
            {
                if (_binding == null)
                {
                    throw new InvalidOperationException($"Must call {nameof(ApplyBindingInternal)} before accessing binding.");
                }

                return _binding;
            }
        }

        /// <summary>
        /// List of all errors and warnings. Check <see cref="ExpressionError.IsWarning"/>.
        /// This can include Parse, Bind, and per-engine custom errors (see <see cref="Engine.PostCheck(CheckResult)"/>, 
        /// or any custom errors passes explicit to the ctor.
        /// Not null, but empty on success.
        /// </summary>
        public IEnumerable<ExpressionError> Errors
        {
            get => GetErrorsInLocale(null);

            [Obsolete("use constructor to set errors")]
            set => _errors.AddRange(value);
        }

        /// <summary>
        /// Get errors localized with the given culture. 
        /// </summary>
        /// <param name="culture"></param>
        /// <returns></returns>
        public IEnumerable<ExpressionError> GetErrorsInLocale(CultureInfo culture)
        {
            culture ??= _defaultErrorCulture ?? ParserCultureInfo;

            foreach (var error in this._errors.Distinct(new ExpressionErrorComparer()))
            {
                yield return error.GetInLocale(culture);
            }
        }

        /// <summary>
        /// True if no errors for stages run so far. 
        /// </summary>
        public bool IsSuccess => !_errors.Any(x => !x.IsWarning);

        /// <summary>
        /// Helper to throw if <see cref="IsSuccess"/> is false.
        /// </summary>
        public void ThrowOnErrors()
        {
            if (!IsSuccess)
            {
                var msg = string.Join("\r\n", Errors.Select(err => err.ToString()).ToArray());
                throw new InvalidOperationException($"Errors: " + msg);
            }
        }

        internal bool HasDeferredArgsWarning => _errors.Any(x => x.IsWarning && x.MessageKey.Equals(TexlStrings.WarnDeferredType.Key, StringComparison.Ordinal));

        private ReadOnlySymbolTable _allSymbols;

        /// <summary>
        /// Full set of Symbols passed to this binding. 
        /// Can include symbols from Config, Engine, and Parameters, 
        /// May be null. 
        /// Set after binding. 
        /// </summary>
        public ReadOnlySymbolTable Symbols
        {
            get
            {
                if (_binding == null)
                {
                    throw new InvalidOperationException($"Must call {nameof(ApplyBinding)} before accessing combined Symbols.");
                }

                return this._allSymbols;
            }
        }

        /// <summary>
        /// Parameters are the subset of symbols that must be passed in Eval() for each evaluation. 
        /// This lets us associated the type in Check()  with the values in Eval().
        /// </summary>
        internal ReadOnlySymbolTable Parameters
        {
            get
            {
                if (!this._setBindingCalled)
                {
                    throw new InvalidOperationException($"Must call {nameof(SetBindingInfo)} first.");
                }

                return _symbols;
            }
        }

        /// <summary>
        /// Culture info used for parsing. 
        /// By default, this is also used for error messages. 
        /// </summary>
        internal CultureInfo ParserCultureInfo { get; private set; }

        internal void ThrowIfSymbolsChanged()
        {
            if (_symbols != null)
            {
                var endHash = _symbols.VersionHash;
                if (_symbolHash != endHash)
                {
                    throw new InvalidOperationException($"SymbolTable was mutated during binding of {_expression}");
                }
            }
        }

        public ParseResult ApplyParse()
        {
            if (_expression == null)
            {
                throw new InvalidOperationException($"Must call {nameof(SetText)} before calling ApplyParse().");
            }

            _beginApply = true;
            if (this.Parse == null)
            {
                var result = Engine.Parse(_expression, Engine.Config.Features, _parserOptions);
                this.Parse = result;

                _errors.AddRange(this.Parse.Errors);
            }

            return this.Parse;
        }

        internal Formula GetParseFormula()
        {
            var parseResult = this.ApplyParse();

            var expression = parseResult.Text;
            var culture = parseResult.Options.Culture;
            var formula = new Formula(expression, culture, intellisenseLocale: _defaultErrorCulture);
            formula.ApplyParse(parseResult);

            return formula;
        }

        /// <summary>
        /// Call to run binding. 
        /// This will compute types on each node, enabling calling <see cref="GetNodeType(TexlNode)"/>.
        /// </summary>
        public void ApplyBinding()
        {
            ApplyBindingInternal();
        }

        // Apply and return the binding. 
        internal TexlBinding ApplyBindingInternal()
        {
            if (_binding == null)
            {
                if (!this._setBindingCalled)
                {
                    throw new InvalidOperationException($"Must call {nameof(SetBindingInfo)} before calling {nameof(ApplyBinding)}.");
                }

                (var binding, var combinedSymbols) = Engine.ComputeBinding(this);

                this.ThrowIfSymbolsChanged();

                // Don't modify any fields until after we've verified the symbols haven't change.

                this._binding = binding;
                this._allSymbols = combinedSymbols;

                // Add the errors
                IEnumerable<ExpressionError> bindingErrors = ExpressionError.New(binding.ErrorContainer.GetErrors(), ParserCultureInfo);
                _errors.AddRange(bindingErrors);

                if (this.IsSuccess)
                {
                    // TODO: Fix FormulaType.Build to not throw exceptions for Enum types then remove this check
                    if (binding.ResultType.Kind != DKind.Enum)
                    {
                        this.ReturnType = FormulaType.Build(binding.ResultType);
                        VerifyReturnTypeMatch();
                    }
                }
            }

            return _binding;
        }

        private void VerifyReturnTypeMatch()
        {
            var ftErrors = new List<ExpressionError>();
            var aggregateTypeChecker = new StrictAggregateTypeChecker(ftErrors);

            FormulaTypeChecker ftChecker;
            if (_expectedReturnTypesCoerces)
            {
                ftChecker = new FormulaTypeCheckerWithCoercion(aggregateTypeChecker, ftErrors);
            }
            else
            {
                ftChecker = new FormulaTypeCheckerNumberCoercionOnly(aggregateTypeChecker, ftErrors);
            }

            ftChecker.Run(this._expectedReturnTypes, this.ReturnType);

            _errors.AddRange(ftErrors);
        }

        /// <summary>
        /// Compute the dependencies. Called after binding. 
        /// </summary>
        [Obsolete("Preview")]
        public DependencyInfo ApplyDependencyInfoScan()
        {
            var ir = ApplyIR(); //throws on errors

            var ctx = new DependencyVisitor.DependencyContext();
            var visitor = new DependencyVisitor();

            // Using the original node without transformations. This simplifies the dependency analysis for PFx.DV side.
            ir.TopOriginalNode.Accept(visitor, ctx);

            return visitor.Info;
        }

        /// <summary>
        /// Compute the dependencies. Called after binding. 
        /// </summary>
        public void ApplyDependencyAnalysis()
        {
            var binding = this.Binding; // will throw if binding wasn't run
            this._topLevelIdentifiers = DependencyFinder.FindDependencies(binding.Top, binding);
        }

        // Flag to ensure Post Checks are only invoked once. 
        private bool _invokingPostCheck;

        /// <summary>
        /// Calculate all errors. 
        /// Invoke Binding and any engine-specific errors via <see cref="Engine.PostCheck(CheckResult)"/>. 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ExpressionError> ApplyErrors()
        {
            if (!_invokingPostCheck)
            {
                _invokingPostCheck = true;

                // Errors require Binding, Parse 
                var binding = ApplyBindingInternal();

                if (this.IsSuccess && _engine.IRTransformList?.Count > 0)
                {
                    // IR alone won't generate errors. 
                    // But if there are additional transforms, those could generate errors. 
                    try
                    {
                        this.ApplyIR();
                    }
                    catch (InvalidOperationException)
                    {
                        // On errors, ApplyIR will add to list and then throw. 
                    }
                }

                // Plus engine's may have additional constraints. 
                // PostCheck may refer to binding. 
                var extraErrors = Engine.InvokePostCheck(this);

                _errors.AddRange(extraErrors);
            }

            return this.Errors;
        }

        internal IRResult ApplyIR()
        {
            if (_irresult == null)
            {
                // IR should not create any new errors. 
                var binding = this.ApplyBindingInternal();
                this.ThrowOnErrors();
                (var irnode, var ruleScopeSymbol) = IRTranslator.Translate(binding);

                var originalIRNode = irnode;

                var list = _engine.IRTransformList;
                if (list != null)
                {
                    foreach (var transform in list)
                    {
                        irnode = transform.Transform(irnode, _errors);

                        // Additional errors from phases. 
                        // Stop any further processing if we have errors. 
                        this.ThrowOnErrors();
                    }
                }

                _irresult = new IRResult
                {
                    TopNode = irnode,
                    TopOriginalNode = originalIRNode,
                    RuleScopeSymbol = ruleScopeSymbol
                };
            }

            return _irresult;
        }

        // pretty print IR for debugging purposes, used by the Console REPL
        public string PrintIR()
        {
            var topNode = this.ApplyIR().TopNode;
            var topStr = topNode.ToString();
            return topStr;
        }

        /// <summary>
        /// Gets the type of a syntax node. Must call <see cref="ApplyBinding"/> first. 
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public FormulaType GetNodeType(TexlNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            var type = this.Binding.GetTypeAllowInvalid(node);
            return FormulaType.Build(type);
        }

        /// <summary>
        /// Get binding data for this call node and determine how it was resolved.
        /// </summary>
        /// <param name="node"></param>
        /// <returns>Null if the node is not bound.</returns>
        public FunctionInfo GetFunctionInfo(Syntax.CallNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            var binding = this.ApplyBindingInternal();
            var info = binding.GetInfo(node);
            var func = info.Function;

            if (func == null)
            {
                return null;
            }

            return new FunctionInfo(func);            
        }

        // Called by language server to get custom tokens.
        // If binding is available, returns context sensitive tokens.  $$$
        // Keeping this temporarily for custom publish tokens notification
        // Feature to auto fix casing of function name in the editor also depends on this function and custom publish tokens notification
        internal IReadOnlyDictionary<string, TokenResultType> GetTokens(GetTokensFlags flags) => GetTokensUtils.GetTokens(this._binding, flags);

        /// <summary>
        /// Returns an enumeration of token text spans in a expression rule with their start and end indices and token type.
        /// </summary>
        /// <param name="tokenTypesToSkip">Optional: Token types that would be skipped and not included in the final result. Usually provided by the language server.</param>
        /// <returns> Enumerable of tokens. Tokens are ordered only if comparer is provided.</returns>
        internal IEnumerable<ITokenTextSpan> GetTokens(IReadOnlyCollection<TokenType> tokenTypesToSkip = null) => Tokenization.Tokenize(_expression, _binding, Parse?.Comments, null, false, tokenTypesToSkip);
        
        /// <summary>
        /// To be called by host without LSP context to get the tokens.
        /// </summary>
        /// <param name="tokenTypesToSkip"></param>
        /// <returns>Enumerable of wrapped tokens.</returns>
        public IEnumerable<TokenTextSpan> GetTextTokens(IReadOnlyCollection<TokenType> tokenTypesToSkip = null) => GetTokens(tokenTypesToSkip).Select(t => t as TokenTextSpan);

        private string _expressionInvariant;

        // form of expression with personal info removed,
        // suitable for logging the structure of a formula.
        private string _expressionAnonymous;

        /// <summary>
        /// Get the invariant form of the expression.  
        /// </summary>
        /// <returns></returns>
        public string ApplyGetInvariant()
        {
            if (_expressionInvariant == null)
            {
                this.GetParseFormula(); // will verify 
                var symbols = this.Parameters; // will throw

                _expressionInvariant = _engine.GetInvariantExpressionWorker(this._expression, symbols, parseCulture: _parserOptions.Culture);
            }

            return _expressionInvariant;
        }

        /// <summary>
        /// Get anonymous form of expression with all PII removed. Suitable for logging to 
        /// capture the structure of the expression.
        /// </summary>
        public string ApplyGetLogging()
        {
            if (_expressionAnonymous == null)
            {
                var parse = ApplyParse();

                _expressionAnonymous = StructuralPrint.Print(parse.Root, _binding);
            }

            return _expressionAnonymous;
        }

        /// <summary>
        /// Get anonymous form of expression with all PII removed. Suitable for logging to.
        /// </summary>
        /// <param name="nameProvider">Sanitizer class to replace string values in the logging result.</param>
        /// <returns></returns>
        internal string ApplyGetLogging(ISanitizedNameProvider nameProvider)
        {
            var parse = ApplyParse();
            return StructuralPrint.Print(parse.Root, _binding, nameProvider);
        }

        public CheckContextSummary ApplyGetContextSummary()
        {
            this.ApplyBinding();

            // $$$ Better check?
            bool isV1 = this._engine.Config.Features == Features.PowerFxV1;
            bool allowSideEffects = this.ApplyParse().Options.AllowsSideEffects;

            // Should contain SymbolProperties
            List<SymbolEntry> symbolEntries = new List<SymbolEntry>();

            if (_engine.TryGetRuleScope(out var ruleScope))
            {
                symbolEntries.Add(new SymbolEntry
                {
                     Name = "ThisRecord",
                     Type = ruleScope
                });
            }
            else
            {
                this._allSymbols?.EnumerateNames(symbolEntries, new ReadOnlySymbolTable.EnumerateNamesOptions());
            }

            var summary = new CheckContextSummary
            {
                AllowsSideEffects = allowSideEffects,
                IsPreV1Semantics = !isV1,
                ExpectedReturnType = this._expectedReturnTypes,
                SuggestedSymbols = symbolEntries
            };

            return summary;
        }

        public IEnumerable<string> GetFunctionNames()
        {
            return GetFunctionNames(false);
        }

        /// <summary>
        /// Get all function names used in the expression.
        /// </summary>
        /// <param name="anonymizeUnknownPublicFunctions">If true, anonymize the name of unknown public functions.</param>
        /// <returns></returns>
        public IEnumerable<string> GetFunctionNames(bool anonymizeUnknownPublicFunctions)
        {
            return GetFunctionNames(anonymizeUnknownPublicFunctions, null);
        }

        /// <summary>
        /// Get all function names used in the expression.
        /// </summary>
        /// <param name="anonymizeUnknownPublicFunctions">If true, anonymize the name of unknown public functions.</param>
        /// <param name="customKnownFunctions">List containing custom functions names that will not be anonymized.</param>
        /// <returns></returns>
        public IEnumerable<string> GetFunctionNames(bool anonymizeUnknownPublicFunctions, ICollection<string> customKnownFunctions)
        {
            return ListFunctionVisitor.Run(ApplyParse(), anonymizeUnknownPublicFunctions, customKnownFunctions);
        }
    }

    // Internal interface to ensure that Result objects have a common contract
    // for error reporting. 
    internal interface IOperationStatus
    {
        public IEnumerable<ExpressionError> Errors { get; }

        public bool IsSuccess { get; }
    }
}
