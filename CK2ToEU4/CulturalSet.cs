using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK2ToEU4
{
    /// <summary>
    /// This class will be used to represent a subculture that hasn't yet been finalised - it is a "cluster" of culturally similar provinces
    /// </summary>
    public class CohesiveSet<T>
    {
        /// <summary>
        /// The content of the set
        /// </summary>
        public List<T> Content { get; set; }
        /// <summary>
        /// The median province - the element closest to all other elements in the set
        /// </summary>
        public T CentralElement { get; set; }


        public Func<T, T, int> GetDistance { get; set; }


        public CohesiveSet(Func<T, T, int> getDistance, params T[] content)
        {
            GetDistance = getDistance;
            Content = content.ToList();
            FindAndSetCentralElement();
        }

        private CohesiveSet(Func<T, T, int> getDistance, CohesiveSet<T> a, CohesiveSet<T> b)
        {
            GetDistance = getDistance;

            Content = new List<T>();
            Content.AddRange(a.Content);
            Content.AddRange(b.Content);
            FindAndSetCentralElement();
        }

        public void FindAndSetCentralElement()
        {
            //set the total distance of our current candidate to the max value, since there is no current candidate
            var centralDistance = int.MaxValue;
            foreach(var el in Content)
            {
                //calculate the sum of the distances to each other elements in the set
                var dist = 0;
                foreach(var el2 in Content)
                {
                    dist += GetDistance(el, el2);
                }
                //if the distance for this element is smaller than the distance for our current candidate, then this is a better candidate
                if(dist < centralDistance)
                {
                    CentralElement = el;
                    centralDistance = dist;
                }
            }
        }

        /// <summary>
        /// Returns the union of this set with another set
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public CohesiveSet<T> Union(CohesiveSet<T> other)
        {
            return new CohesiveSet<T>(GetDistance, this, other);
        }

        /// <summary>
        /// Calculates a metric for the cohesion for the group - the scale is such that a larger number is less cohesive (hence "anticohesion")
        /// </summary>
        /// <returns></returns>
        public int CalculateAnticohesion()
        {
            var dist = 0;
            foreach (var el in Content)
            {
                dist += GetDistance.Invoke(el, CentralElement);
            }

            return dist;// / Content.Count;
        }


    }
}
