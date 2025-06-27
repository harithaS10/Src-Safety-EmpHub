using Microsoft.Data.SqlClient;
using System.Data;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Drawing;
namespace SafetyToolBoxAPI.Controllers
{

    public class Geo
    {     
        public static bool checkPointExistsInGeofencePolygon(string latlnglist, string lat, string lng)
        {
            List<Loc> objList = new List<Loc>();            
            string[] arr = latlnglist.Split('|');
            for (int i = 0; i <= arr.Length - 1; i++)
            {
                string latlng = arr[i];
                string[] arrlatlng = latlng.Split(',');

                Loc er = new Loc(Convert.ToDouble(arrlatlng[0]), Convert.ToDouble(arrlatlng[1]));
                objList.Add(er);
            }
            Loc pt = new Loc(Convert.ToDouble(lat), Convert.ToDouble(lng));

            if (IsPointInPolygon(objList, pt) == true)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        private static bool IsPointInPolygon(List<Loc> poly, Loc point)
        {
            int i, j;
            bool c = false;
            for (i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((((poly[i].Lt <= point.Lt) && (point.Lt < poly[j].Lt)) |
                    ((poly[j].Lt <= point.Lt) && (point.Lt < poly[i].Lt))) &&
                    (point.Lg < (poly[j].Lg - poly[i].Lg) * (point.Lt - poly[i].Lt) / (poly[j].Lt - poly[i].Lt) + poly[i].Lg))
                    c = !c;
            }
            return c;
        }
    }
    public class Loc
        {
            private double lt;
            private double lg;

            public double Lg
            {
                get { return lg; }
                set { lg = value; }
            }

            public double Lt
            {
                get { return lt; }
                set { lt = value; }
            }

            public Loc(double lt, double lg)
            {
                this.lt = lt;
                this.lg = lg;
            }
        }
}
