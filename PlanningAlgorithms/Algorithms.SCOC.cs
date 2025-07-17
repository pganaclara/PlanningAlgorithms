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
        public static AbstractEvent[] SCOC(ISchedulingProblem problem, int products, int mpsString, int limiar = 100, bool controllableFirst = true)
        {
            var initial = problem.InitialState;
            var target = problem.TargetState;
            var resOrig = problem.InitialRestrition(mpsString);
            var depth = mpsString * problem.Depth;
            var uncontrollables = problem.Events.Where(e => !e.IsControllable).ToSet();
            var transitions = problem.Transitions;

            IDictionary<AbstractState, Context> frontier = new Dictionary<AbstractState, Context> { { initial, (ImmutableList<AbstractEvent>.Empty, resOrig, 0f) } };

            for (var i = 0; i < depth; i++)
            {
                var newFrontier = new ConcurrentDictionary<AbstractState, Context>();

                void Loop(KeyValuePair<AbstractState, Context> kvp)
                {
                    var (q1, (sequence1, res1, parallelism1)) = kvp;

                    var events = res1.Enabled;
                    events.UnionWith(uncontrollables);
                    events.IntersectWith(transitions[q1].Keys);

                    if (controllableFirst && events.Any(e => e.IsControllable)) events.ExceptWith(uncontrollables);

                    foreach (var e in events)
                    {
                        var q2 = transitions[q1][e];

                        var parallelism2 = parallelism1 + q2.ActiveTasks();
                        var sequence2 = sequence1.Add(e);
                        var res2 = e.IsControllable ? res1.Update(e) : res1;
                        var context2 = (sequence2, res2, parallelism2);

                        newFrontier.AddOrUpdate(q2, context2, (oldq, oldc) => oldc.Item3 < parallelism2 ? context2 : oldc);

                    }
                }

                if (frontier.Count > limiar) Partitioner.Create(frontier, EnumerablePartitionerOptions.NoBuffering).AsParallel().ForAll(Loop);
                else foreach (var kvp in frontier) Loop(kvp);

                frontier = newFrontier;

                Debug.WriteLine($"Frontier: {frontier.Count} elements");

            }

            if (!frontier.ContainsKey(target))
                throw new Exception($"The algorithm could not reach the targer ({target})");

            var allElements = frontier[target].Item1.ToArray();
            var controllables = frontier[target].Item1.Where(e => e.IsControllable).ToArray();

            if (products == 1)
            {
                return allElements;

            }
            else
            {
                var concatenatedArray = controllables.Concat(controllables).ToArray();
                var maskedSequence = GenerateMaskedSequence(mpsString, controllables.Length);
                int initialSize = concatenatedArray.Length / 4;
                int productionSize = (mpsString % 2 == 0) ? 2 * initialSize : 2 * initialSize + 1;

                var initialPhase = concatenatedArray.Take(initialSize).ToArray(); 
                var productionPhase = concatenatedArray.Skip(initialSize).Take(productionSize).ToArray(); 
                var finalPhase = concatenatedArray.Skip(initialSize + productionSize).ToArray();

                var bestSolution = VNSConcat(initialPhase, productionPhase, finalPhase, products, problem, maskedSequence, finalPhase.Length, mpsString, 20, 10, 20);

                return bestSolution;
            }
        }

        static int[] GenerateMaskedSequence(int numberOfMpsStrings, int sequenceLength)
        {
            int[] maskedSequence = new int[sequenceLength];

            int numberOfZeros = sequenceLength - sequenceLength/2;

            for (int i = 0; i < numberOfZeros; i++)
            {
                maskedSequence[i] = 0;
            }
            for (int i = numberOfZeros; i < sequenceLength; i++)
            {
                maskedSequence[i] = 1;
            }

            return maskedSequence;
        }

        static AbstractEvent[] VNSConcat(AbstractEvent[] initialPhase, AbstractEvent[] productionPhase, AbstractEvent[] finalPhase, int products, ISchedulingProblem problem, int[] maskedSequence, int finalPhaseLength, int mpsString, int maxNoImprovement, int maxIterations, int kMax)
        {
            int noImprovementCount = 0;
            int iteration = 0;
            var firstHalf = productionPhase.Take(finalPhaseLength).ToArray();
            var array = GenerateFinalArrayConcat(initialPhase, productionPhase, firstHalf, finalPhase, products, maskedSequence, mpsString);
            var (currentMakespan, currentArray) = PlanningDES.Tools.TimeEvaluationControllable(problem, array);

            while (iteration < maxIterations && noImprovementCount < maxNoImprovement)
            {
                int k = 1;
                while (k < kMax)
                {
                    (AbstractEvent[] newSolution, int[] productionSequence) = TwoOptSwapConcat(productionPhase, problem, finalPhaseLength);
                    var arrayBeforeVerification = GenerateFinalArrayConcat(initialPhase, newSolution, firstHalf, finalPhase, products, productionSequence, mpsString);
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

        static AbstractEvent[] GenerateFinalArrayConcat(AbstractEvent[] initialPhase, AbstractEvent[] productionPhase, AbstractEvent[] firstHalf, AbstractEvent[] finalPhase, int numberOfProducts, int[] productionSequence, int mpsString)
        {
            int productionRepeatCount = (numberOfProducts % 2 == 0) ? (numberOfProducts - 1) / 2 : numberOfProducts / 2;
            productionRepeatCount = productionRepeatCount/mpsString;
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

        public static (AbstractEvent[] shuffledSequence, int[] groupIndices) TwoOptSwapConcat(AbstractEvent[] productionPhase, ISchedulingProblem problem, int finalPhaseLength)
        {
            Random random = new Random();

            AbstractEvent[] group0 = productionPhase.Take(finalPhaseLength).ToArray();
            AbstractEvent[] group1 = productionPhase.Skip(finalPhaseLength).ToArray();

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
