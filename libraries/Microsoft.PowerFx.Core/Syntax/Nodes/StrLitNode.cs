//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.AppMagic.Authoring.Texl.SourceInformation;

namespace Microsoft.AppMagic.Authoring.Texl
{
    internal sealed class StrLitNode : TexlNode
    {
        public readonly string Value;

        public StrLitNode(ref int idNext, StrLitToken tok)
            : base(ref idNext, tok, new SourceList(tok))
        {
            Value = tok.Value;
            Contracts.AssertValue(Value);
        }

        public override TexlNode Clone(ref int idNext, Span ts)
        {
            return new StrLitNode(ref idNext, Token.Clone(ts).As<StrLitToken>());
        }

        public override void Accept(TexlVisitor visitor)
        {
            Contracts.AssertValue(visitor);
            visitor.Visit(this);
        }

        public override Result Accept<Result, Context>(TexlFunctionalVisitor<Result, Context> visitor, Context context)
        {
            return visitor.Visit(this, context);
        }

        public override NodeKind Kind { get { return NodeKind.StrLit; } }

        public override StrLitNode AsStrLit()
        {
            return this;
        }
    }
}