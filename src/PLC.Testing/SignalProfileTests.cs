using System;
using PLC.Shared.Models;
using PLC.Worker.SignalGeneration;
using FluentAssertions;
using Xunit;

namespace PLC.Testing;

public class SignalProfileTests
{
    [Fact]
    public void Static_ReturnsConstantValue()
    {
        var profile = new StaticProfile(new SimulationConfig { Value = 42.5 });
        profile.ComputeValue(0, null).Should().Be(42.5);
        profile.ComputeValue(99999, 42.5).Should().Be(42.5);
    }

    [Fact]
    public void Step_IncrementsByStep()
    {
        var config = new SimulationConfig
        {
            Profile = "Step", Step = 10, LowLimit = 0, HighLimit = 100,
            UpdateMs = 1000, Direction = "Up", AtLimit = "Wrap"
        };
        var profile = new StepProfile(config);
        var val = 0.0;
        for (int i = 0; i < 5; i++)
        {
            val = (double)profile.ComputeValue(i * 1000, val);
        }
        val.Should().Be(50);
    }

    [Fact]
    public void Ramp_ProducesSawtooth()
    {
        var config = new SimulationConfig { LowLimit = 0, HighLimit = 100, PeriodMs = 10000, UpdateMs = 100 };
        var profile = new RampProfile(config);
        var valAtMid = (double)profile.ComputeValue(5000, null);
        valAtMid.Should().BeApproximately(50, 1);
        // At exact period boundary, t=0 so value wraps to low limit
        var valAtWrap = (double)profile.ComputeValue(10000, null);
        valAtWrap.Should().BeApproximately(0, 0.01);
        var valAfter = (double)profile.ComputeValue(10001, null);
        valAfter.Should().BeApproximately(0.01, 0.01);
    }

    [Fact]
    public void Sine_WithinBounds()
    {
        var config = new SimulationConfig { LowLimit = -10, HighLimit = 10, PeriodMs = 10000, UpdateMs = 100 };
        var profile = new SineProfile(config);
        for (int t = 0; t < 20000; t += 100)
        {
            var val = (double)profile.ComputeValue(t, null);
            val.Should().BeInRange(-10.5, 10.5); // allow small noise
        }
    }

    [Fact]
    public void Sine_WithNoise_IsDeterministicAcrossInstances()
    {
        var config = new SimulationConfig { LowLimit = 0, HighLimit = 100, PeriodMs = 10000, UpdateMs = 100, NoisePercent = 20, Seed = 42 };
        var profile1 = new SineProfile(config);
        var profile2 = new SineProfile(config);
        var val1 = (double)profile1.ComputeValue(1000, null);
        var val2 = (double)profile2.ComputeValue(1000, null);
        val1.Should().BeApproximately(val2, 0.001);
    }

    [Fact]
    public void Square_ProducesCorrectDutyCycle()
    {
        var config = new SimulationConfig { LowLimit = 0, HighLimit = 100, PeriodMs = 1000, DutyPercent = 30 };
        var profile = new SquareProfile(config);
        var onVal = (double)profile.ComputeValue(100, null);
        var offVal = (double)profile.ComputeValue(500, null);
        onVal.Should().Be(100); // within 30% duty
        offVal.Should().Be(0);  // past 30%
    }

    [Fact]
    public void Triangle_ProducesPeakAtMidPeriod()
    {
        var config = new SimulationConfig { LowLimit = 0, HighLimit = 100, PeriodMs = 10000 };
        var profile = new TriangleProfile(config);
        var valAtQuarter = (double)profile.ComputeValue(2500, null);
        var valAtMid = (double)profile.ComputeValue(5000, null);
        valAtQuarter.Should().BeApproximately(50, 5);
        valAtMid.Should().BeApproximately(100, 5);
    }

    [Fact]
    public void Random_WithSeed_Deterministic()
    {
        var config = new SimulationConfig { LowLimit = 0, HighLimit = 100, Distribution = "uniform", Seed = 12345 };
        var profile1 = new RandomProfile(config);
        var profile2 = new RandomProfile(config);
        var val1 = (double)profile1.ComputeValue(1000, null);
        var val2 = (double)profile2.ComputeValue(1000, null);
        val1.Should().BeApproximately(val2, 0.001);
    }

    [Fact]
    public void Toggle_Alternates()
    {
        var config = new SimulationConfig { IntervalMs = 1000 };
        var profile = new ToggleProfile(config);
        ((bool)profile.ComputeValue(0, null)).Should().BeTrue();
        ((bool)profile.ComputeValue(500, null)).Should().BeTrue();
        ((bool)profile.ComputeValue(1500, null)).Should().BeFalse();
        ((bool)profile.ComputeValue(2500, null)).Should().BeTrue();
    }

    [Fact]
    public void Pulse_ProducesCorrectOnTime()
    {
        var config = new SimulationConfig { PeriodMs = 1000, DutyPercent = 25 };
        var profile = new PulseProfile(config);
        ((bool)profile.ComputeValue(100, null)).Should().BeTrue();
        ((bool)profile.ComputeValue(300, null)).Should().BeFalse();
    }

    [Fact]
    public void Counter_IncrementsAndRollsOver()
    {
        var config = new SimulationConfig { Step = 2, RolloverAt = 10 };
        var profile = new CounterProfile(config);
        var val = profile.ComputeValue(0, null);
        val.Should().Be(2.0);
        val = profile.ComputeValue(100, (double)val);
        val.Should().Be(4.0);
        // 8 + 2 = 10 triggers rollover (>= 10)
        val = profile.ComputeValue(1000, 8.0);
        val.Should().Be(0.0);
        // starts from 0 again
        val = profile.ComputeValue(1100, (double)val);
        val.Should().Be(2.0);
    }

    [Fact]
    public void Clock_ProducesFormattedString()
    {
        var config = new SimulationConfig { Format = "HH:mm:ss" };
        var profile = new ClockProfile(config);
        var val = (string)profile.ComputeValue(0, null);
        // Time separator varies by culture, accept any non-digit separator
        val.Should().MatchRegex(@"^\d{2}\D\d{2}\D\d{2}$");
    }

    [Fact]
    public void TextCycle_IteratesValues()
    {
        var config = new SimulationConfig { Values = new() { "A", "B", "C" }, IntervalMs = 1000 };
        var profile = new TextCycleProfile(config);
        ((string)profile.ComputeValue(0, null)).Should().Be("A");
        ((string)profile.ComputeValue(1000, null)).Should().Be("B");
        ((string)profile.ComputeValue(2000, null)).Should().Be("C");
        ((string)profile.ComputeValue(3000, null)).Should().Be("A");
    }

    [Fact]
    public void Echo_ReturnsPreviousValue()
    {
        var config = new SimulationConfig { Value = 99 };
        var profile = new EchoProfile(config);
        profile.ComputeValue(0, null).Should().Be(99);
        profile.ComputeValue(100, 42).Should().Be(42);
    }

    [Fact]
    public void ProfileFactory_CreatesCorrectTypes()
    {
        ProfileFactory.Create(new SimulationConfig { Profile = "Static" }).Should().BeOfType<StaticProfile>();
        ProfileFactory.Create(new SimulationConfig { Profile = "Step" }).Should().BeOfType<StepProfile>();
        ProfileFactory.Create(new SimulationConfig { Profile = "Sine" }).Should().BeOfType<SineProfile>();
        ProfileFactory.Create(new SimulationConfig { Profile = "Echo" }).Should().BeOfType<EchoProfile>();
        ProfileFactory.Create(new SimulationConfig { Profile = "unknown" }).Should().BeOfType<StaticProfile>();
    }
}
