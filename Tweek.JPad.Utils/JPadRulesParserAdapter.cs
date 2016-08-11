﻿using Engine.Core.Context;
using Engine.Core.Rules;
using Engine.DataTypes;
using Tweek.JPad;
using LanguageExt;
using System;

namespace Tweek.JPad.Utils
{
    public class AnonymousRule : IRule
    {
        private readonly Func<GetContextValue, Option<ConfigurationValue>> fn;

        public AnonymousRule(Func<GetContextValue, Option<ConfigurationValue>> fn)
        {
            this.fn = fn;
        }

        public Option<ConfigurationValue> GetValue(GetContextValue fullContext) => fn(fullContext);
    }

    public class AnonymousParser : IRuleParser
    {
        private readonly Func<string, IRule> fn;

        public AnonymousParser(Func<string, IRule> fn)
        {
            this.fn = fn;
        }

        public IRule Parse(string source) => fn(source);
    }

    public class JPadRulesParserAdapter
    {
        public static IRuleParser Convert(JPadParser parser)
        {
            return new AnonymousParser((source) =>
            {
                var compiled = parser.Parse.Invoke(source);
                return new AnonymousRule(context =>
                    FSharp.fs(compiled.Invoke((s) => context.Invoke(s).ToFSharp()))
                            .Map(ConfigurationValue.New));
            });
        }
    }
}