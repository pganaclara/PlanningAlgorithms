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
using Context = System.ValueTuple<System.Collections.Immutable.ImmutableList<UltraDES.AbstractEvent>, PlanningDES.Restriction, float>;
using UltraDES.PetriNets;


namespace PlanningAlgorithms
{
    public static partial class Algorithms
    {
        public static AbstractEvent[] SCOC(ISchedulingProblem problem, int products, int limiar = 100, bool controllableFirst = true)
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

            var allElements = frontier[target].OrderBy(c => c.Item4).First().Item1.ToArray(); //all elements
            var controllables = frontier[target].OrderBy(c => c.Item4).First().Item1.Where(e => e.IsControllable).ToArray(); //controllables

            if (products == 1)
            {
                return allElements;
            }
            else
            {
                var concatenatedArray = ConcatenateArrayConcat(controllables, 2);
                int initialSize = concatenatedArray.Length / 4;
                int productionSize = 2 * initialSize + 1;

                var initialPhase = concatenatedArray.Take(initialSize).ToArray(); //first 11 events of group0
                var productionPhase = concatenatedArray.Skip(initialSize).Take(productionSize).ToArray(); //12 events of group0 and 11 of group1
                var finalPhase = concatenatedArray.Skip(initialSize + productionSize).ToArray(); //12 events of group1

                var bestSolution = VNSConcat(initialPhase, productionPhase, finalPhase, products, problem);

                return bestSolution;
            }
        }

        static AbstractEvent[] ConcatenateArrayConcat(AbstractEvent[] array, int products)
        {
            return Enumerable.Repeat(array, products).SelectMany(x => x).ToArray();
        }

        static AbstractEvent[] VNSConcat(AbstractEvent[] initialPhase, AbstractEvent[] productionPhase, AbstractEvent[] finalPhase, int products, ISchedulingProblem problem)
        {
            int noImprovementCount = 0;
            int maxNoImprovement = 20;
            int iteration = 0;
            int maxIterations = 5;
            int kMax = 20;
            int[] initialProductionSequence = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
            var firstHalf = productionPhase.Take(12).ToArray();
            var array = GenerateFinalArrayConcat(initialPhase, productionPhase, firstHalf, finalPhase, products, initialProductionSequence);
            var (currentMakespan, currentArray) = PlanningDES.Tools.TimeEvaluationControllable(problem, array);

            while (iteration < maxIterations && noImprovementCount < maxNoImprovement)
            {
                int k = 1;
                while (k < kMax)
                {
                    (AbstractEvent[] newSolution, int[] productionSequence) = TwoOptSwapConcat(initialPhase, productionPhase, problem);
                    var arrayBeforeVerification = GenerateFinalArrayConcat(initialPhase, newSolution, firstHalf, finalPhase, products, productionSequence);
                    var finalArray = LGSE(arrayBeforeVerification, problem);
                    var (newMakespan, newArray) = PlanningDES.Tools.TimeEvaluationControllable(problem, finalArray);

                    if (newMakespan < currentMakespan)
                    {
                        currentArray = newArray;
                        currentMakespan = newMakespan;
                        noImprovementCount = 0;
                        break;
                    }
                    else
                    {
                        k++;
                    }
                }
                if (k > kMax)
                {
                    noImprovementCount++;
                }
                iteration++;
            }

            return currentArray;
        }

        static AbstractEvent[] GenerateFinalArrayConcat(AbstractEvent[] initialPhase, AbstractEvent[] productionPhase, AbstractEvent[] firstHalf, AbstractEvent[] finalPhase, int numberOfProducts, int[] productionSequence)
        {
            int productionRepeatCount = (numberOfProducts % 2 == 0) ? (numberOfProducts - 1) / 2 : numberOfProducts / 2;
            var reversedSequence = productionSequence.Select(x => (x == 0) ? 1 : 0).ToArray(); //create ipattern inverting 0s and 1s
            var reversedProductionPhase = ConcatenateGroupsConcat(initialPhase, finalPhase, reversedSequence); //create the sequence based on the reversed sequence with 0s and 1s

            var finalArray = new List<AbstractEvent>();

            finalArray.AddRange(initialPhase);

            for (int i = 0; i < productionRepeatCount; i++)
            {
                if (numberOfProducts != 2)
                {
                    finalArray.AddRange(productionPhase);
                    finalArray.AddRange(reversedProductionPhase);
                }
            }

            if (numberOfProducts % 2 == 0)
            {
                finalArray.AddRange(productionPhase);
                finalArray.AddRange(finalPhase); //if even finishes with 1s
            }
            else
            {
                finalArray.AddRange(firstHalf); //if odd finishes with 0s
            }

            return finalArray.ToArray();
        }

        public static AbstractEvent[] ConcatenateGroupsConcat(AbstractEvent[] group0, AbstractEvent[] group1, int[] groupIndices)
        {
            int group0Length = 0;
            int group1Length = 0;
            List<AbstractEvent> concatenatedSequence = new List<AbstractEvent>();

            foreach (int groupIndex in groupIndices)
            {
                AbstractEvent[] currentGroup = (groupIndex == 0) ? group0 : group1;

                if (currentGroup == group0)
                {
                    concatenatedSequence.Add(group0[group0Length]);
                    group0Length++;
                }
                else
                {
                    concatenatedSequence.Add(group1[group1Length]);
                    group1Length++;
                }
            }

            return concatenatedSequence.ToArray();
        }

        public static (AbstractEvent[] shuffledSequence, int[] groupIndices) TwoOptSwapConcat(AbstractEvent[] initialPhase, AbstractEvent[] productionPhase, ISchedulingProblem problem)
        {
            Random random = new Random();

            AbstractEvent[] group0 = productionPhase.Take(12).ToArray();
            AbstractEvent[] group1 = productionPhase.Skip(12).ToArray();

            int group0Index = 0;
            int group1Index = 0;

            List<AbstractEvent> shuffledEvents = new List<AbstractEvent>();
            List<int> groupIndices = new List<int>();

            while (group0Index < group0.Length || group1Index < group1.Length)
            {
                if (group0Index < group0.Length && (group1Index >= group1.Length || random.Next(2) == 0))
                {
                    shuffledEvents.Add(group0[group0Index]);
                    groupIndices.Add(0); // Group 0 index
                    group0Index++;
                }
                else
                {
                    shuffledEvents.Add(group1[group1Index]);
                    groupIndices.Add(1); // Group 1 index
                    group1Index++;
                }
            }

            return (shuffledEvents.ToArray(), groupIndices.ToArray());
        }

        static AbstractEvent[] LGSE(AbstractEvent[] arrayBeforeVerification, ISchedulingProblem problem)
        {
            var finiteAutomaton = problem.Supervisor.Projection(problem.Supervisor.UncontrollableEvents);
            var transitions = finiteAutomaton.Transitions.GroupBy(t => t.Origin)
                .ToDictionary(g => g.Key, g => g.ToDictionary(t => t.Trigger, t => t.Destination));
            var currentState = finiteAutomaton.InitialState;
            List<AbstractEvent> postponedEvents = new List<AbstractEvent>();
            List<AbstractEvent> newArray = new List<AbstractEvent>();

            foreach (var eventsInitialPhase in arrayBeforeVerification)
            {
                try
                {
                    currentState = transitions[currentState][eventsInitialPhase];
                    newArray.Add(eventsInitialPhase);
                }
                catch (Exception)
                {
                    var trans = transitions[currentState].Keys;
                    if (!trans.Contains(eventsInitialPhase))
                    {
                        postponedEvents.Add(eventsInitialPhase);
                    }
                }

                for (int i = 0; i < postponedEvents.Count; i++)
                {
                    var e = postponedEvents[i];
                    var trans = transitions[currentState].Keys;
                    if (trans.Contains(e))
                    {
                        try
                        {
                            currentState = transitions[currentState][e];
                            newArray.Add(e);
                            postponedEvents.RemoveAt(i);
                            i--;
                        }
                        catch (Exception)
                        {
                            continue;
                        }
                    }
                }
            }

            return newArray.ToArray();

        }
    }
}
