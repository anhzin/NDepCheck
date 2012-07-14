﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NDepCheck {
    public class DependencyRuleGroup : Pattern {
        private static readonly Comparison<DependencyRule> _sortOnDescendingHitCount = (r1, r2) => r2.HitCount - r1.HitCount;

        private readonly List<DependencyRule> _allowed;
        private readonly List<DependencyRule> _questionable;
        private readonly List<DependencyRule> _forbidden;

        private readonly string _group;
        private readonly List<string> _groupRegexes;

        private DependencyRuleGroup(string group,
                    IEnumerable<DependencyRule> allowed,
                    IEnumerable<DependencyRule> questionable,
                    IEnumerable<DependencyRule> forbidden) {
            _group = group;
            _groupRegexes = group == "" ? null : Expand(group);
            _allowed = allowed.ToList();
            _questionable = questionable.ToList();
            _forbidden = forbidden.ToList();
        }

        public DependencyRuleGroup(string group)
            : this(group,
                Enumerable.Empty<DependencyRule>(),
                Enumerable.Empty<DependencyRule>(),
                Enumerable.Empty<DependencyRule>()) {
            // empty
        }

        public string Group {
            get {
                return _group;
            }
        }
        /// <summary>
        /// Add one or more <c>DependencyRules</c>s from a single input
        /// line.
        /// public for testability.
        /// </summary>
        public void AddDependencyRules(DependencyRuleSet parent, string ruleFileName, uint lineNo, string line) {
            if (line.Contains(DependencyRuleSet.MAYUSE)) {
                foreach (var rule in CreateDependencyRules(parent, ruleFileName, lineNo, line, DependencyRuleSet.MAYUSE, false)) {
                    Add(_allowed, rule);
                }
            } else if (line.Contains(DependencyRuleSet.MAYUSE_WITH_WARNING)) {
                foreach (var rule in CreateDependencyRules(parent, ruleFileName, lineNo, line, DependencyRuleSet.MAYUSE_WITH_WARNING, true)) {
                    Add(_questionable, rule);
                }
            } else if (line.Contains(DependencyRuleSet.MUSTNOTUSE)) {
                foreach (var rule in CreateDependencyRules(parent, ruleFileName, lineNo, line, DependencyRuleSet.MUSTNOTUSE, false)) {
                    Add(_forbidden, rule);
                }
            } else {
                throw new ApplicationException("Unexpected rule at " + ruleFileName + ":" + lineNo);
            }
        }

        private static void Add(List<DependencyRule> ruleList, DependencyRule rule) {
            if (!ruleList.Any(r => r.IsSameAs(rule))) {
                ruleList.Add(rule);
            }
        }

        private static IEnumerable<DependencyRule> CreateDependencyRules(DependencyRuleSet parent, string ruleFileName, uint lineNo, string line, string sep, bool questionableRule) {
            DependencyRuleRepresentation rep = new DependencyRuleRepresentation(ruleFileName, lineNo, line, questionableRule);
            int i = line.IndexOf(sep);
            string usingPattern = parent.ExpandDefines(line.Substring(0, i).Trim());
            string usedPattern = parent.ExpandDefines(line.Substring(i + sep.Length).Trim());
            List<DependencyRule> deps = DependencyRule.CreateDependencyRules(usingPattern, usedPattern, rep);

            if (parent.Verbose) {
                Log.Info("Rules used for checking {0} ({1}:{2})", line, ruleFileName, lineNo);
                foreach (DependencyRule d in deps) {
                    Log.Info("  " + d);
                }
            }
            return deps;
        }

        public DependencyRuleGroup Combine(DependencyRuleGroup other) {
            return new DependencyRuleGroup(_group,
                _allowed.Union(other._allowed),
                _questionable.Union(other._questionable),
                _forbidden.Union(other._forbidden));
        }

        public bool Check(IEnumerable<Dependency> dependencies, bool verbose, bool debug) {
            bool result = true;
            int reorgCount = 0;
            int nextReorg = 200;

            foreach (Dependency d in dependencies) {
                // The group is the globale one (""); or the dependency's left side
                // matches the group's pattern:
                Dependency d1 = d;
                if (_groupRegexes == null || _groupRegexes.Any(r => Regex.IsMatch(d1.UsingItem, r))) {
                    if (_groupRegexes != null) {
                        _groupRegexes.ToString();
                    }
                    result &= Check(d, verbose, debug);
                    if (++reorgCount > nextReorg) {
                        _forbidden.Sort(_sortOnDescendingHitCount);
                        _allowed.Sort(_sortOnDescendingHitCount);
                        _questionable.Sort(_sortOnDescendingHitCount);
                        nextReorg = 6 * nextReorg / 5 + 200;
                    }
                }
            }

            return result;
        }

        private bool Check(Dependency d, bool verbose, bool debug) {
            bool ok = false;
            if (verbose) {
                Log.Info("Checking " + d);
            }
            foreach (DependencyRule r in _forbidden) {
                if (r.Matches(d, debug)) {
                    goto DONE;
                }
            }
            foreach (DependencyRule r in _allowed) {
                if (r.Matches(d, debug)) {
                    ok = true;
                    goto DONE;
                }
            }
            foreach (DependencyRule r in _questionable) {
                if (r.Matches(d, debug)) {
                    Log.WriteWarning("Dependency " + d + " is questionable", d.FileName, d.StartLine,
                                                       d.StartColumn, d.EndLine, d.EndColumn);
                    ok = true;
                    goto DONE;
                }
            }
        DONE:
            if (!ok) {
                Log.WriteError(d.IllegalMessage(), d.FileName, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn);
            }
            return ok;
        }
    }
}