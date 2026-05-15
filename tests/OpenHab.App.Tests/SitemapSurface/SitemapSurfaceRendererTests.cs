#if ENABLE_WINUI_RENDERER_TESTS
using System.Reflection;
using System.Reflection.Emit;
using OpenHab.Windows.Tray.Rendering;
using OpenHab.Windows.Tray.Rendering.SitemapSurface;

namespace OpenHab.App.Tests.SitemapSurface;

// Reflecting over the WinUI renderer type can keep the plain xUnit CLI testhost
// alive after assertions finish. Keep these implementation-detail checks opt-in
// until there is a WinUI-specific test host.
public sealed class SitemapSurfaceRendererTests
{
    [Fact]
    public void PartialRowUpdate_DoesNotSnapVisibilityAfterUpdateState()
    {
        var method = typeof(SitemapSurfaceRenderer).GetMethod(
            "ApplyPartialRowUpdate",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.True(MethodCalls(method!, nameof(SitemapControlFactory.UpdateState)));
        Assert.False(MethodCalls(method!, nameof(SitemapControlFactory.SetVisibility)));
    }

    [Fact]
    public void StructuralReconcile_UsesCollapseAndRemoveForDisappearingRows()
    {
        var method = typeof(SitemapSurfaceRenderer).GetMethod(
            "ReconcileStructuralRows",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.True(MethodCalls(method!, nameof(SitemapControlFactory.CollapseAndRemove)));
    }

    private static bool MethodCalls(MethodInfo method, string calledMethodName)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null)
        {
            return false;
        }

        for (var index = 0; index < il.Length;)
        {
            var opcode = ReadOpCode(il, ref index);
            if (opcode is null)
            {
                break;
            }

            if (opcode.Value.OperandType == OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(il, index);
                index += 4;
                try
                {
                    if (method.Module.ResolveMethod(token)?.Name == calledMethodName)
                    {
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                    // Ignore metadata tokens that cannot be resolved in this test context.
                }

                continue;
            }

            index += OperandSize(opcode.Value.OperandType, il, index);
        }

        return false;
    }

    private static OpCode? ReadOpCode(byte[] il, ref int index)
    {
        if (index >= il.Length)
        {
            return null;
        }

        var value = il[index++];
        if (value != 0xFE)
        {
            return SingleByteOpCodes[value];
        }

        if (index >= il.Length)
        {
            return null;
        }

        return MultiByteOpCodes[il[index++]];
    }

    private static int OperandSize(OperandType operandType, byte[] il, int operandIndex) =>
        operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget => 1,
            OperandType.ShortInlineI => 1,
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI => 4,
            OperandType.InlineBrTarget => 4,
            OperandType.InlineField => 4,
            OperandType.InlineMethod => 4,
            OperandType.InlineSig => 4,
            OperandType.InlineString => 4,
            OperandType.InlineTok => 4,
            OperandType.InlineType => 4,
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 => 8,
            OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, operandIndex)),
            _ => 0
        };

    private static readonly OpCode[] SingleByteOpCodes = BuildOpCodeLookup(singleByte: true);
    private static readonly OpCode[] MultiByteOpCodes = BuildOpCodeLookup(singleByte: false);

    private static OpCode[] BuildOpCodeLookup(bool singleByte)
    {
        var lookup = new OpCode[256];
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opcode)
            {
                continue;
            }

            var value = (ushort)opcode.Value;
            if (singleByte && value <= 0xFF)
            {
                lookup[value] = opcode;
            }
            else if (!singleByte && (value & 0xFF00) == 0xFE00)
            {
                lookup[value & 0xFF] = opcode;
            }
        }

        return lookup;
    }
}
#endif
