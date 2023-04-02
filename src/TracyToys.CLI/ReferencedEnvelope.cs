internal record ReferencedEnvelope(double LonMin, double LonMax, double LatMin, double LatMax)
{
    public ReferencedEnvelope Intersection(ReferencedEnvelope envelope)
    {
        return new ReferencedEnvelope(
                Math.Max(this.LonMin, envelope.LonMin),
                Math.Min(this.LonMax, envelope.LonMax),
                Math.Max(this.LatMin, envelope.LatMin),
                Math.Min(this.LatMax, envelope.LatMax)
            );
    }
}