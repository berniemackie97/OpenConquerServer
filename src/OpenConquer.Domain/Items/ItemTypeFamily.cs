namespace OpenConquer.Domain.Items;

public static class ItemTypeFamily
{
    public const int NotEquipment = -1;

    public static int ResolveCategory(uint itemTypeId)
    {
        int withinMillion = checked((int)(itemTypeId % 1_000_000u));
        int thousandsGroup = withinMillion / 1_000;

        switch (thousandsGroup)
        {
            case 123: return 1;
            case 203: return 0x15;
            case 300 when itemTypeId % 10u == 0: return 0x0E;
            case 601: return 4;
            case 201:
            case 202: return 0x0C;
            case 200: return 0x14;
            case 350: return 0x10;
            case 360: return 0x0F;
            case 370: return 0x11;
            case 380: return 0x12;
        }

        int hundredThousands = checked((int)(itemTypeId % 10_000_000u / 100_000u));

        return hundredThousands switch
        {
            7 => 9,
            4 or 6 => 4,
            5 => 5,
            9 => 6,
            10 => 0,
            >= 20 and <= 29 => 0x0A,
            1 => (withinMillion / 10_000) switch
            {
                11 or 14 or 17 => 1,
                12 => 2,
                13 => 3,
                15 => 7,
                16 => 8,
                18 or 19 => 0x0B,
                _ => NotEquipment,
            },
            _ => NotEquipment,
        };
    }

    public static int ResolveSubKind(uint itemTypeId)
    {
        int withinMillion = checked((int)(itemTypeId % 1_000_000u));
        int thousandsGroup = withinMillion / 1_000;

        if (thousandsGroup is 201 or 202)
        {
            return checked((int)(itemTypeId % 10_000u / 1_000u));
        }

        int hundredThousands = checked((int)(itemTypeId % 10_000_000u / 100_000u));

        return hundredThousands switch
        {
            4 or 5 => checked((int)(itemTypeId % 100_000u / 1_000u * 1_000u)),
            7 or 10 => checked((int)(itemTypeId % 100_000u / 10_000u * 10_000u)),
            _ => NotEquipment,
        };
    }

    public static bool IsBow(uint itemTypeId)
    {
        return ResolveCategory(itemTypeId) == 5 && ResolveSubKind(itemTypeId) == 0;
    }

    public static bool IsArrow(uint itemTypeId)
    {
        return ResolveCategory(itemTypeId) == 0 && ResolveSubKind(itemTypeId) == 50_000;
    }
}
