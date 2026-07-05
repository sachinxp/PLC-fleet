using System;
using PLC.Shared.Models;

namespace PLC.Worker.SignalGeneration;

public static class ProfileFactory
{
    public static IProfileGenerator Create(SimulationConfig config)
    {
        return config.Profile.ToLowerInvariant() switch
        {
            "static" => new StaticProfile(config),
            "step" => new StepProfile(config),
            "ramp" => new RampProfile(config),
            "sine" => new SineProfile(config),
            "cosine" => new CosineProfile(config),
            "square" => new SquareProfile(config),
            "triangle" => new TriangleProfile(config),
            "random" => new RandomProfile(config),
            "toggle" => new ToggleProfile(config),
            "pulse" => new PulseProfile(config),
            "counter" => new CounterProfile(config),
            "clock" => new ClockProfile(config),
            "textcycle" => new TextCycleProfile(config),
            "echo" => new EchoProfile(config),
            "noise" => new NoiseProfile(config),
            "randomwalk" => new RandomWalkProfile(config),
            _ => new StaticProfile(config)
        };
    }
}
