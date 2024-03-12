using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using PlanningDES;
using UltraDES;
using static System.Runtime.InteropServices.JavaScript.JSType;
using TimeContext = System.ValueTuple<System.Collections.Immutable.ImmutableList<UltraDES.AbstractEvent>, PlanningDES.Scheduler, PlanningDES.Restriction, float>;

namespace PlanningAlgorithms
{
    public static partial class Algorithms
    {
        public static AbstractEvent[] SCO(ISchedulingProblem problem, int products, int limiar = 100, bool controllableFirst = true)
        {
            var initial = problem.InitialState;

            var target = problem.TargetState;
            var schOrig = problem.InitialScheduler;
            var resOrig = problem.InitialRestrition(1); //1 product
            var depth = 1 * problem.Depth; // 1 product
            var uncontrollables = problem.Events.Where(e => !e.IsControllable).ToSet();
            var transitions = problem.Transitions;

            var frontier = new Dictionary<AbstractState, List<TimeContext>>
            {
                {initial, new List<TimeContext>{(ImmutableList<AbstractEvent>.Empty, schOrig, resOrig, 0f)}}
            };

            for (var it = 0; it < depth; it++)
            {
                var newFrontier = new Dictionary<AbstractState, List<TimeContext>>(frontier.Count);

                void Loop(KeyValuePair<AbstractState, List<TimeContext>> kvp)
                {
                    var q1 = kvp.Key;
                    foreach (var (sequence1, sch1, res1, time1) in kvp.Value)
                    {
                        var events = res1.Enabled;
                        events.UnionWith(uncontrollables);
                        events.IntersectWith(sch1.Enabled);
                        events.IntersectWith(transitions[q1].Keys);

                        if (controllableFirst && events.Any(e => e.IsControllable)) events.ExceptWith(uncontrollables);

                        foreach (var e in events)
                        {
                            var q2 = transitions[q1][e];

                            var sequence2 = sequence1.Add(e);
                            var time2 = time1 + sch1[e];
                            var res2 = e.IsControllable ? res1.Update(e) : res1;
                            var sch2 = sch1.Update(e);

                            var context2 = (sequence2, sch2, res2, time2);

                            var contextLst = new List<TimeContext>();

                            lock (newFrontier)
                            {
                                if (!newFrontier.ContainsKey(q2)) newFrontier.Add(q2, contextLst);
                                else contextLst = newFrontier[q2];
                            }

                            lock (contextLst)
                            {
                                var candidate = contextLst.Where(c =>
                                {
                                    var (_, sch, res, _) = c;
                                    return problem.Events.All(ev => sch2[ev] == sch[ev]); //&& problem.Events.All(ev => res2[ev] == res[ev]);
                                }).ToList();

                                if (candidate.Any() && candidate.Single().Item4 <= time2) continue;
                                if (candidate.Any() && candidate.Single().Item4 > time2) contextLst.Remove(candidate.Single());
                                contextLst.Add(context2);
                            }
                        }
                    }
                }

                if (frontier.Count > limiar) Partitioner.Create(frontier, EnumerablePartitionerOptions.NoBuffering).AsParallel().ForAll(Loop);
                else foreach (var kvp in frontier) Loop(kvp);

                frontier = newFrontier;

                Debug.WriteLine($"Frontier: {frontier.Count} elements");
            }

            if (!frontier.ContainsKey(target))
                throw new Exception($"The algorithm could not reach the targer ({target})");

            AbstractEvent[] group0 = frontier[target].OrderBy(c => c.Item4).First().Item1.ToArray(); //concat group0 and group1 
            AbstractEvent[] group1 = frontier[target].OrderBy(c => c.Item4).First().Item1.ToArray(); //concat group0 and group1 

            if (products == 1)
            {
                return group1;
            } else
            {
                AbstractEvent[] concatenatedArray = ConcatenateArrays(group0, group1, 2);
                 int initialSize = concatenatedArray.Length / 4;
                 int productionSize = 2 * initialSize;
                

                   AbstractEvent[] initialPhase = concatenatedArray.Take(initialSize).ToArray(); //0s
                   AbstractEvent[] productionPhase = concatenatedArray.Skip(initialSize).Take(productionSize).ToArray(); //first half 0s, scnd 1s
                   AbstractEvent[] finalPhase = concatenatedArray.Skip(initialSize + productionSize).ToArray(); //1s

                   AbstractEvent[] finalArray = GenerateFinalArray(initialPhase, productionPhase, finalPhase, products);
                return finalArray;

            }
        }

        static AbstractEvent[] GenerateFinalArray(AbstractEvent[] initialPhase, AbstractEvent[] productionPhase, AbstractEvent[] finalPhase, int numberOfProducts)
        {
            int productionRepeatCount = numberOfProducts / 2;
            int midpoint = productionPhase.Length / 2;

            AbstractEvent[] firstHalf = productionPhase.Take(midpoint).ToArray(); //0s
            AbstractEvent[] secondHalf = productionPhase.Skip(midpoint).ToArray(); //1s
            AbstractEvent[] reversedProductionPhase = finalPhase.Concat(initialPhase).ToArray();

            var finalArray = new List<AbstractEvent>();

            finalArray.AddRange(initialPhase);

            for (int i = 0; i < productionRepeatCount; i++)
            {
                if(numberOfProducts != 2)
                {
                    finalArray.AddRange(productionPhase);
                    finalArray.AddRange(reversedProductionPhase);
                }
            }

            if (numberOfProducts % 2 == 0)
            {
                finalArray.AddRange(productionPhase);
                finalArray.AddRange(finalPhase);
            } else
            {
                finalArray.AddRange(reversedProductionPhase);
                finalArray.AddRange(firstHalf);
            }

            return finalArray.ToArray();
        }



        static AbstractEvent[] ConcatenateArrays(AbstractEvent[] array0, AbstractEvent[] array1, int products)
        {
            int length0 = array0.Length;
            int length1 = array1.Length;

            AbstractEvent[] concatenatedArray = Enumerable.Range(0, products)
                                          .SelectMany(i => i % 2 == 0 ? array0 : array1)
                                          .ToArray();

            return concatenatedArray;
        }
    }
}
