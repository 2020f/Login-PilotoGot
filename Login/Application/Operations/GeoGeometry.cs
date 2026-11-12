namespace Login.Application.Operations;

public static class GeoGeometry
{
    public static bool Valid(GeoPoint p) => double.IsFinite(p.Latitud) && double.IsFinite(p.Longitud) && Math.Abs(p.Latitud) <= 90 && Math.Abs(p.Longitud) <= 180;
    public static List<GeoPoint> Validate(List<GeoPoint>? source)
    {
        if (source is null || source.Count < 3 || source.Count > 500 || source.Any(p => !Valid(p))) throw new OperationsException(400, "Dibuja un polígono válido de 3 a 500 puntos.");
        var p = source.ToList(); if (p[0] == p[^1]) p.RemoveAt(p.Count - 1);
        if (p.Distinct().Count() != p.Count || p.Count < 3 || Area(p) < .000001) throw new OperationsException(400, "La zona requiere un área y vértices distintos.");
        for (var i = 0; i < p.Count; i++) for (var j = i + 1; j < p.Count; j++)
        {
            if (j == i + 1 || (i == 0 && j == p.Count - 1)) continue;
            if (Intersects(p[i], p[(i + 1) % p.Count], p[j], p[(j + 1) % p.Count])) throw new OperationsException(400, "Los límites de la zona no pueden cruzarse.");
        }
        return p;
    }
    private static double Cross(GeoPoint a, GeoPoint b, GeoPoint p) => (b.Longitud - a.Longitud) * (p.Latitud - a.Latitud) - (b.Latitud - a.Latitud) * (p.Longitud - a.Longitud);
    private static bool On(GeoPoint a, GeoPoint b, GeoPoint p) => Math.Abs(Cross(a, b, p)) < 1e-10 && p.Longitud >= Math.Min(a.Longitud, b.Longitud) && p.Longitud <= Math.Max(a.Longitud, b.Longitud) && p.Latitud >= Math.Min(a.Latitud, b.Latitud) && p.Latitud <= Math.Max(a.Latitud, b.Latitud);
    private static bool Intersects(GeoPoint a, GeoPoint b, GeoPoint c, GeoPoint d) => On(a, b, c) || On(a, b, d) || On(c, d, a) || On(c, d, b) || (Cross(a, b, c) * Cross(a, b, d) < 0 && Cross(c, d, a) * Cross(c, d, b) < 0);
    public static bool Contains(IReadOnlyList<GeoPoint> polygon, GeoPoint point)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            if (On(a, b, point)) return true;
            if ((a.Latitud > point.Latitud) != (b.Latitud > point.Latitud) && point.Longitud < (b.Longitud - a.Longitud) * (point.Latitud - a.Latitud) / (b.Latitud - a.Latitud) + a.Longitud) inside = !inside;
        }
        return inside;
    }
    public static double Area(IReadOnlyList<GeoPoint> p)
    {
        const double rad = Math.PI / 180;
        var sum = 0d;
        for (var i = 0; i < p.Count; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; sum += (b.Longitud - a.Longitud) * rad * (2 + Math.Sin(a.Latitud * rad) + Math.Sin(b.Latitud * rad)); }
        return Math.Round(Math.Abs(sum) * 6371.0088 * 6371.0088 / 2, 6);
    }
}
