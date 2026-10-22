namespace RustyChemistry.Checks;

/// <summary>Small authored packs, each just enough to exercise an operator.</summary>
internal static class Packs
{
    /// <summary>Grass fire, smoke venting to the sky, dousing, and binding that resists ignition.</summary>
    internal const string Fire = """
    {
      "quantities": [
        { "id": "heat", "ambient": 20, "min": -50, "max": 1500, "settle": 0.05, "epsilon": 0.01, "transfer": { "rate": 3 } },
        { "id": "moisture", "max": 1, "epsilon": 0.001, "transfer": { "rate": 0.2 } },
        { "id": "smoke", "max": 10, "epsilon": 0.001, "transfer": { "rate": 2, "drift": true } },
        { "id": "fuel" },
        { "id": "bind", "max": 100 }
      ],
      "materials": [
        { "id": "air", "sky": true, "conduct": { "heat": 0.05, "smoke": 1, "moisture": 0.1 } },
        { "id": "stone", "conduct": { "heat": 0.02 } },
        { "id": "grass", "sky": true, "conduct": { "heat": 0.1, "smoke": 0.5, "moisture": 0.5 }, "initial": { "fuel": 6 } },
        { "id": "burning-grass", "sky": true, "conduct": { "heat": 0.1, "smoke": 0.5, "moisture": 0.5 } },
        { "id": "ash", "sky": true, "conduct": { "heat": 0.05, "smoke": 0.5, "moisture": 0.5 } }
      ],
      "reactions": [
        { "id": "combustion", "in": ["burning-grass"], "rate": 1, "modulator": "fire",
          "consumes": { "fuel": 1 }, "produces": { "heat": 1200, "smoke": 2 } },
        { "id": "smoke-vents", "when": [{ "open": true }], "rate": 2, "per": "smoke", "consumes": { "smoke": 1 } }
      ],
      "transforms": [
        { "id": "ignite", "from": ["grass"], "to": "burning-grass", "chance": 3, "resistedBy": "bind",
          "when": [{ "quantity": "heat", "atLeast": 250 }, { "quantity": "moisture", "atMost": 0.2 }] },
        { "id": "douse", "from": ["burning-grass"], "to": "grass", "when": [{ "quantity": "moisture", "atLeast": 0.5 }] },
        { "id": "burn-out", "from": ["burning-grass"], "to": "ash", "when": [{ "quantity": "fuel", "atMost": 0 }] },
      ],
    }
    """;

    /// <summary>One diffusing quantity, a conducting and a sealed material, and a hold that confines.</summary>
    internal const string Diffusion = """
    {
      "quantities": [
        { "id": "heat", "epsilon": 0.0001, "transfer": { "rate": 5, "blockedBy": "hold" } },
        { "id": "hold", "max": 1 }
      ],
      "materials": [
        { "id": "air", "conduct": { "heat": 1 } },
        { "id": "wall" }
      ]
    }
    """;

    /// <summary>Charge that discharges through water, and a blast front that crosses air and brush but not stone.</summary>
    internal const string Impulse = """
    {
      "quantities": [
        { "id": "charge", "max": 100, "settle": 2, "epsilon": 0.01 },
        { "id": "blast", "max": 100, "settle": 5, "epsilon": 0.01 }
      ],
      "materials": [
        { "id": "air", "conduct": { "blast": 1 } },
        { "id": "brush", "conduct": { "blast": 0.5 } },
        { "id": "stone" },
        { "id": "dirt", "conduct": { "blast": 1 } },
        { "id": "water", "conduct": { "charge": 0.9, "blast": 1 } }
      ]
    }
    """;
}
