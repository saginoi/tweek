﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Engine.Core.Context;
using Engine.Core.Rules;
using Engine.DataTypes;
using Engine.Match.DSL;
using Engine.Rules;
using Engine.Rules.Creation;
using Engine.Rules.ValueDistribution;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tweek.JPad.Rules;

namespace Tweek.JPad
{
    public delegate ConfigurationValue ValueDistributor(params object[] units);
    public delegate bool Matcher(GetContextValue fullContext);

    public class JPadParser : IRuleParser
    {
        
        ValueDistributor ParseValueDistrubtor(JToken schema)
        {
            var valueDistributor = ValueDistribution.compile_ext(schema.ToString());
            return units => new ConfigurationValue(valueDistributor(units));
        }

        Matcher ParseMatcher(JToken schema)
        {
            return context =>
            {
                return MatchDSL.Match_ext(schema.ToString(), key => context(key).IfNoneUnsafe((string)null));
            };
        }

        private IRule ParseRule(RuleData data)
        {
            var matcher = ParseMatcher(data.Matcher);

            if (data.Type == "SingleVariant")
            {
                return new SingleVariantRule
                {
                    Matcher = matcher,
                    Value = new ConfigurationValue(data.Value)
                };
            }
            if (data.Type == "MultiVariant")
            {

                var valueDistributors = new SortedList<DateTimeOffset, ValueDistributor>(
                    data.ValueDistribution.ToDictionary(x => x.Key, x => ParseValueDistrubtor(x.Value)));

                return new MultiVariantRule
                {
                    OwnerType = data.OwnerType,
                    ExperimentId = data.Id,
                    Matcher = matcher,
                    ValueDistributors = valueDistributors
                };
            }
            throw new Exception("no parser for rule type");
        }

        private IRule FallBack(IRule l, IRule r)
        {
            return new FallbackRule(l, r);
        }

        public IRule Parse(string text)
        {
            var rules = JsonConvert.DeserializeObject<List<RuleData>>(text);
            return rules.Select(ParseRule).Reduce(FallBack);

        }
    }
}