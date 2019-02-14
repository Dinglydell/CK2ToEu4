using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK2ToEU4
{
    /// <summary>
    /// This class will take a list of things and "cluster" them into sets of similar things by a given metric of distance
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SetGenerator<T>
    {
        public List<T> Elements { get; set; }
        public Func<T,T,int> GetDistance { get; set; }
        public SetGenerator(IEnumerable<T> elements, Func<T, T, int> getDistance)
        {
            GetDistance = getDistance;
            Elements = elements.ToList();
        }

        public List<CohesiveSet<T>> GenerateSets(int anticohesionThreshold)
        {
            var sets = Elements.Select(s => new CohesiveSet<T>(GetDistance, s)).ToList();
            // repeatedly carry out the best possible merge until we go above the anticohesion threshhold
            while (MergeClosestSet(sets, anticohesionThreshold));

            return sets;
        }
        /// <summary>
        /// Carries out the most cohesive possible merge out of the set of all clusters. Returns true if merged completed (anticohesion is less than the threshold)
        /// </summary>
        /// <param name="set"></param>
        /// <returns></returns>
        private bool MergeClosestSet(List<CohesiveSet<T>> set, int anticohesionThreshold)
        {
            var mergeIndexA = -1;
            var mergeIndexB = -1;
            CohesiveSet<T> mergedSet = null;
            var anticohesion = int.MaxValue;
            //iterate over every pair to find the one that would be the most cohesive
            for (var i = 0; i < set.Count; i++)
            {
                var setA = set[i];
                for (var j = i + 1; j < set.Count; j++)
                {
                    var setB = set[j];
                    var mergeSet = setA.Union(setB);
                    var myAnticohesion = mergeSet.CalculateAnticohesion();
                    //if I am less anticohesive, ie more cohesive then I am a better candidate
                    if(myAnticohesion < anticohesion)
                    {
                        mergeIndexA = i;
                        mergeIndexB = j;
                        mergedSet = mergeSet;
                        anticohesion = myAnticohesion;
                    }
                }
            }
            if (anticohesion < anticohesionThreshold)
            {
                set[mergeIndexA] = mergedSet;
                set.RemoveAt(mergeIndexB);
                return true;
            }
            return false;
        }

    }
}
