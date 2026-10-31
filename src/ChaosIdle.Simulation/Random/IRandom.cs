namespace ChaosIdle.Simulation.Random
{
    /// <summary>Random source used by all simulation code. Never use System.Random in rules code.</summary>
    public interface IRandom
    {
        /// <summary>Uniform value in [0, 1).</summary>
        double NextDouble();
    }
}
