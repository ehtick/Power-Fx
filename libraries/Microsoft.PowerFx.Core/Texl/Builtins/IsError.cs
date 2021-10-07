//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.AppMagic.Authoring.Texl
{
    // IsError(value: any)
    internal sealed class IsErrorFunction : BuiltinFunction
    {
        public override bool IsSelfContained => true;

        public IsErrorFunction()
            : base("IsError", TexlStrings.AboutIsError, FunctionCategories.Logical, DType.Boolean, 0, 1, 1)
        { }

        public override IEnumerable<TexlStrings.StringGetter[]> GetSignatures()
        {
            yield return new[] { TexlStrings.IsErrorArg };
        }

        public override bool CheckInvocation(TexlBinding binding, TexlNode[] args, DType[] argTypes, IErrorContainer errors, out DType returnType)
        {
            Contracts.AssertValue(binding);
            Contracts.AssertValue(args);
            Contracts.AssertValue(argTypes);
            Contracts.Assert(args.Length == argTypes.Length);
            Contracts.Assert(args.Length == 1);
            Contracts.AssertValue(errors);

            DType type = ReturnType;

            Contracts.Assert(ReturnType == DType.Boolean);

            returnType = ReturnType;
            return true;
        }
    }
}