using System.Text;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using static NativeCIL.IR.IROpCode;
using static NativeCIL.IR.IRRegister;

namespace NativeCIL.IR;

public class IRCompiler
{
    private readonly ModuleDefMD _module;
    private readonly int _bitnessFlag;
    private readonly StringBuilder _ldstr;

    public readonly string AssemblyName;
    public readonly int PointerSize;
    public readonly Settings Settings;
    public readonly List<IRInstruction> Instructions;

    public IRCompiler(ref Settings settings)
    {
        _module = ModuleDefMD.Load(settings.InputFile);
        _ldstr = new();

        if (settings.Architecture == TargetArchitecture.Amd64)
        {
            _bitnessFlag = IRFlag.Qword;
            PointerSize = 8;
        }
        else
        {
            _bitnessFlag = IRFlag.Dword;
            PointerSize = 4;
        }

        AssemblyName = GetSafeName(_module.Assembly.Name);
        Settings = settings;
        Instructions = new();
    }

    public void Compile()
    {
        // Initialize static constructors
        foreach (var type in _module.Types)
            foreach (var method in type.Methods)
                if (method.IsConstructor || method.IsStaticConstructor)
                    AddInstruction(Call, IRFlag.Label, GetSafeName(method.FullName));

        // Jump to entry point
        AddInstruction(Call, IRFlag.Label, GetSafeName(_module.EntryPoint.FullName));

        foreach (var type in _module.Types)
        {
            // Initialize static fields
            foreach (var field in type.Fields)
                if (field.IsStatic)
                    AddInstruction(Label, _bitnessFlag, GetSafeName(field.FullName), field.HasConstant ? field.Constant.Value : 0);

            // Compile methods
            foreach (var method in type.Methods)
            {
                AddInstruction(Label, -1, GetSafeName(method.FullName));

                var branches = GetAllBranches(method).ToList();
                foreach (var inst in method.Body.Instructions)
                {
                    foreach (var branch in branches)
                        if (((Instruction)branch.Operand).Offset == inst.Offset)
                        {
                            AddInstruction(Label, -1, BrLabelName(inst, method, true));
                            break;
                        }

                    AddInstruction(Comment, -1, inst.OpCode);
                    switch (inst.OpCode.Code)
                    {
                        case Code.Nop: AddInstruction(Nop); break;
                        case Code.Ret: AddInstruction(Ret); break;

                        case Code.Pop:
                            AddInstruction(Sub, _bitnessFlag | IRFlag.DestRegister | IRFlag.Immediate, R0, PointerSize);
                            break;

                        case Code.Ldc_I4_0: Push(0); break;
                        case Code.Ldc_I4_1: Push(1); break;
                        case Code.Ldc_I4_2: Push(2); break;
                        case Code.Ldc_I4_3: Push(3); break;
                        case Code.Ldc_I4_4: Push(4); break;
                        case Code.Ldc_I4_5: Push(5); break;
                        case Code.Ldc_I4_6: Push(6); break;
                        case Code.Ldc_I4_7: Push(7); break;
                        case Code.Ldc_I4_8: Push(8); break;
                        case Code.Ldc_I4_M1: Push(-1); break;

                        case Code.Ldc_I4_S:
                        case Code.Ldc_I4:
                        case Code.Ldc_I8:
                            Push(inst.Operand);
                            break;

                        case Code.Conv_I1:
                        case Code.Conv_U1:
                            Pop(R1);
                            AddInstruction(And, _bitnessFlag | IRFlag.DestRegister | IRFlag.Immediate, R1, 0xFF);
                            Push(R1);
                            break;

                        case Code.Conv_I2:
                        case Code.Conv_U2:
                            Pop(R1);
                            AddInstruction(And, _bitnessFlag | IRFlag.DestRegister | IRFlag.Immediate, R1, 0xFFFF);
                            Push(R1);
                            break;

                        case Code.Conv_I4:
                        case Code.Conv_I:
                        case Code.Conv_U4:
                        case Code.Conv_U:
                            if (PointerSize == 8)
                            {
                                Pop(R1);
                                AddInstruction(And, _bitnessFlag | IRFlag.DestRegister | IRFlag.Immediate, R1, 0xFFFFFFFF);
                                Push(R1);
                            }
                            break;

                        case Code.Conv_I8:
                        case Code.Conv_U8:
                            // TODO: Is this right?
                            if (PointerSize == 4)
                            {
                                Pop(R1);
                                AddInstruction(And, _bitnessFlag | IRFlag.DestRegister | IRFlag.Immediate, R1, 0xFFFFFFFFFFFFFFFF);
                                Push(R1);
                            }
                            break;

                        // TODO of code generation (stind.i), use R1.Qword
                        case Code.Stind_I1:
                            Pop(R1); // Value
                            Pop(R2); // Address
                            AddInstruction(Mov, IRFlag.DestRegister | IRFlag.DestPointer, PointerSize == 8 ? R2.Qword : R2.Dword, R1.Byte);
                            break;
                        case Code.Stind_I2:
                            Pop(R1); // Value
                            Pop(R2); // Address
                            AddInstruction(Mov, IRFlag.DestRegister | IRFlag.DestPointer, PointerSize == 8 ? R2.Qword : R2.Dword, R1.Word);
                            break;
                        case Code.Stind_I4:
                            Pop(R1); // Value
                            Pop(R2); // Address
                            AddInstruction(Mov, IRFlag.DestRegister | IRFlag.DestPointer, PointerSize == 8 ? R2.Qword : R2.Dword, R1.Dword);
                            break;
                        case Code.Stind_I8:
                            Pop(R1); // Value
                            Pop(R2); // Address
                            AddInstruction(Mov, IRFlag.DestRegister | IRFlag.DestPointer, PointerSize == 8 ? R2.Qword : R2.Dword, R1.Qword);
                            break;

                        case Code.Ldind_U1:
                        case Code.Ldind_I1:
                            Pop(R1);
                            AddInstruction(Mov, IRFlag.SrcRegister | IRFlag.SrcPointer | _bitnessFlag, R2, R1);
                            Push(R2);
                            goto case Code.Conv_I1;
                        case Code.Ldind_U2:
                        case Code.Ldind_I2:
                            Pop(R1);
                            AddInstruction(Mov, IRFlag.SrcRegister | IRFlag.SrcPointer | _bitnessFlag, R2, R1);
                            Push(R2);
                            goto case Code.Conv_I2;
                        case Code.Ldind_U4:
                        case Code.Ldind_I4:
                            Pop(R1);
                            AddInstruction(Mov, IRFlag.SrcRegister | IRFlag.SrcPointer | _bitnessFlag, R2, R1);
                            Push(R2);
                            goto case Code.Conv_I4;
                        case Code.Ldind_I8:
                            Pop(R1);
                            AddInstruction(Mov, IRFlag.SrcRegister | IRFlag.SrcPointer | _bitnessFlag, R2, R1);
                            Push(R2);
                            break;

                        case Code.Add:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Add, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            Push(R2);
                            break;

                        case Code.And:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(And, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            Push(R2);
                            break;

                        case Code.Sub:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Sub, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            Push(R2);
                            break;

                        case Code.Mul:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Mul, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            Push(R2);
                            break;

                        case Code.Or:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Or, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            Push(R2);
                            break;

                        case Code.Xor:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Xor, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            Push(R2);
                            break;

                        case Code.Shl:
                            Pop(R5);
                            Pop(R2);
                            AddInstruction(Shl, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R5.Byte);
                            Push(R2);
                            break;

                        case Code.Shr:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Shr, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            Push(R2);
                            break;

                        case Code.Ldloc_0:
                            PopIndex(0, R1, R3);
                            Push(R1);
                            break;
                        case Code.Ldloc_1:
                            PopIndex(1, R1, R3);
                            Push(R1);
                            break;
                        case Code.Ldloc_2:
                            PopIndex(2, R1, R3);
                            Push(R1);
                            break;
                        case Code.Ldloc_3:
                            PopIndex(3, R1, R3);
                            Push(R1);
                            break;

                        case Code.Ldstr:
                            var str = (string)inst.Operand;
                            var bytes = Encoding.Unicode.GetBytes(str);
                            var name = "LB_" + bytes.GetHashCode().ToString("X4");
                            var next = "LB_" + str.GetHashCode().ToString("X4");

                            _ldstr.Clear();
                            for (var i = 0; i < bytes.Length; i++)
                                _ldstr.Append(bytes[i] + (i + 1 == bytes.Length ? string.Empty : ","));

                            AddInstruction(Mov, IRFlag.DestRegister | IRFlag.SrcPointer | IRFlag.Label | _bitnessFlag, R1, name);
                            AddInstruction(Add, IRFlag.DestRegister | IRFlag.Immediate | _bitnessFlag, R0, PointerSize);
                            AddInstruction(Mov, IRFlag.DestRegister | IRFlag.DestPointer | _bitnessFlag, R0, R1);
                            AddInstruction(Jmp, IRFlag.Label, next);
                            AddInstruction(Label, -1, name);
                            AddInstruction(Store, IRFlag.Byte, _ldstr.ToString());
                            AddInstruction(Label, -1, next);
                            break;

                        case Code.Ldloc_S:
                        case Code.Ldloc:
                            PopIndex(inst.Operand is Local o ? o.Index : Convert.ToInt32(inst.Operand), R1, R3);
                            Push(R1);
                            break;

                        case Code.Stloc_0:
                            Pop(R1);
                            PushIndex(0, R1, R3);
                            break;
                        case Code.Stloc_1:
                            Pop(R1);
                            PushIndex(1, R1, R3);
                            break;
                        case Code.Stloc_2:
                            Pop(R1);
                            PushIndex(2, R1, R3);
                            break;
                        case Code.Stloc_3:
                            Pop(R1);
                            PushIndex(3, R1, R3);
                            break;

                        case Code.Stloc_S:
                        case Code.Stloc:
                            Pop(R1);
                            PushIndex(inst.Operand is Local u ? u.Index : Convert.ToInt32(inst.Operand), R1, R3);
                            break;

                        case Code.Dup:
                            Peek(R1);
                            Push(R1);
                            break;

                        case Code.Br_S:
                        case Code.Br:
                            AddInstruction(Jmp, IRFlag.Label, BrLabelName(inst, method));
                            break;

                        case Code.Brtrue_S:
                        case Code.Brtrue:
                            Pop(R1);
                            AddInstruction(Cmp, IRFlag.DestRegister | IRFlag.Immediate | _bitnessFlag, R1, 0);
                            AddInstruction(Jmp, IRFlag.Label | IRFlag.NotZero, BrLabelName(inst, method));
                            break;

                        case Code.Blt_Un_S:
                        case Code.Blt_Un:
                        case Code.Blt_S:
                        case Code.Blt:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Cmp, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            AddInstruction(Jmp, IRFlag.Label | IRFlag.Less, BrLabelName(inst, method));
                            break;

                        case Code.Beq_S:
                        case Code.Beq:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Cmp, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            AddInstruction(Jmp, IRFlag.Label | IRFlag.Equal, BrLabelName(inst, method));
                            break;

                        case Code.Bge_Un_S:
                        case Code.Bge_Un:
                        case Code.Bge_S:
                        case Code.Bge:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Cmp, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            AddInstruction(Jmp, IRFlag.Label | IRFlag.GreaterOrEqual, BrLabelName(inst, method));
                            break;

                        case Code.Bgt_Un_S:
                        case Code.Bgt_Un:
                        case Code.Bgt_S:
                        case Code.Bgt:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Cmp, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            AddInstruction(Jmp, IRFlag.Label | IRFlag.Greater, BrLabelName(inst, method));
                            break;

                        case Code.Ble_Un_S:
                        case Code.Ble_Un:
                        case Code.Ble_S:
                        case Code.Ble:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Cmp, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            AddInstruction(Jmp, IRFlag.Label | IRFlag.LessOrEqual, BrLabelName(inst, method));
                            break;

                        case Code.Bne_Un_S:
                        case Code.Bne_Un:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Cmp, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            AddInstruction(Jmp, IRFlag.Label | IRFlag.NotEqual, BrLabelName(inst, method));
                            break;

                        case Code.Brfalse_S:
                        case Code.Brfalse:
                            Pop(R1);
                            AddInstruction(Cmp, IRFlag.DestRegister | IRFlag.Immediate | _bitnessFlag, R1, 0);
                            AddInstruction(Jmp, IRFlag.Label | IRFlag.Zero, BrLabelName(inst, method));
                            break;

                        case Code.Clt_Un:
                        case Code.Clt:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Cmp, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            AddInstruction(Set, IRFlag.DestRegister | IRFlag.Less | IRFlag.Byte, R2);
                            Push(R2);
                            break;

                        case Code.Cgt_Un:
                        case Code.Cgt:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Cmp, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            AddInstruction(Set, IRFlag.DestRegister | IRFlag.Greater | IRFlag.Byte, R2);
                            Push(R2);
                            break;

                        case Code.Ceq:
                            Pop(R1);
                            Pop(R2);
                            AddInstruction(Cmp, IRFlag.DestRegister | IRFlag.SrcRegister | _bitnessFlag, R2, R1);
                            AddInstruction(Set, IRFlag.DestRegister | IRFlag.Equal | IRFlag.Byte, R2);
                            Push(R2);
                            break;

                        case Code.Call:
                            var meth = (MethodDef)inst.Operand;
                            for (var i = meth.Parameters.Count; i > 0; i--)
                            {
                                Pop(R1);
                                PushIndex(i - 1, R1, R4);
                            }

                            AddInstruction(Call, IRFlag.Label, GetSafeName(meth.FullName));
                            break;

                        case Code.Ldarg_S:
                        case Code.Ldarg:
                            PopIndex(Convert.ToInt32(inst.Operand), R1, R4);
                            Push(R1);
                            break;

                        case Code.Ldarg_0:
                            PopIndex(0, R1, R4);
                            Push(R1);
                            break;
                        case Code.Ldarg_1:
                            PopIndex(1, R1, R4);
                            Push(R1);
                            break;
                        case Code.Ldarg_2:
                            PopIndex(2, R1, R4);
                            Push(R1);
                            break;
                        case Code.Ldarg_3:
                            PopIndex(3, R1, R4);
                            Push(R1);
                            break;

                        case Code.Ldsfld:
                            PopString(GetSafeName(((FieldDef)inst.Operand).FullName), R1);
                            Push(R1);
                            break;

                        case Code.Stsfld:
                            Pop(R1);
                            PushString(GetSafeName(((FieldDef)inst.Operand).FullName), R1);
                            break;

                        default:
                            Console.WriteLine("Unimplemented opcode: " + inst.OpCode);
                            break;
                    }
                }
            }
        }
    }

    private static string GetSafeName(string name) => Regex.Replace(name, @"[^0-9a-zA-Z]+", "_");

    private static string BrLabelName(Instruction ins, MethodDef def, bool create = false) =>
        $"LB_{def.GetHashCode():X4}{(create ? ins.Offset : ((Instruction)ins.Operand).Offset):X4}";

    private static IEnumerable<Instruction> GetAllBranches(MethodDef method)
    {
        foreach (var br in method.Body.Instructions)
            if (br.OpCode.OperandType is OperandType.InlineBrTarget or OperandType.ShortInlineBrTarget)
                yield return br;
    }

    private void AddInstruction(IROpCode opCode, int flags = -1, object? operand1 = null, object? operand2 = null) =>
        Instructions.Add(new IRInstruction(opCode, flags, operand1, operand2));

    private void PushIndex(int index, object obj, IRRegister reg) =>
        AddInstruction(Mov, IRFlag.DestRegister | IRFlag.DestPointer | _bitnessFlag, reg + index * PointerSize, obj);

    private void PopIndex(int index, IRRegister dst, IRRegister src) =>
        AddInstruction(Mov,
            IRFlag.DestRegister | IRFlag.SrcRegister | IRFlag.SrcPointer | _bitnessFlag, dst, src + index * PointerSize);

    private void PushString(string str, IRRegister reg) =>
        AddInstruction(Mov, IRFlag.DestPointer | IRFlag.Label | IRFlag.SrcRegister | _bitnessFlag, str, reg);

    private void PopString(string str, IRRegister reg) =>
        AddInstruction(Mov, IRFlag.SrcPointer | IRFlag.Label | IRFlag.DestRegister | _bitnessFlag, reg, str);

    private void Peek(IRRegister reg)
        => AddInstruction(Mov, IRFlag.DestRegister | IRFlag.SrcRegister | IRFlag.SrcPointer | _bitnessFlag, reg, R0);

    private void Push(object imm)
    {
        AddInstruction(Add, IRFlag.DestRegister | IRFlag.Immediate | _bitnessFlag, R0, PointerSize);
        AddInstruction(Mov, IRFlag.DestRegister | IRFlag.DestPointer | _bitnessFlag | IRFlag.Immediate, R0, imm);
    }

    private void Push(IRRegister reg)
    {
        AddInstruction(Add, IRFlag.DestRegister | IRFlag.Immediate | _bitnessFlag, R0, PointerSize);
        AddInstruction(Mov, IRFlag.DestRegister | IRFlag.DestPointer | IRFlag.SrcRegister | _bitnessFlag, R0, reg);
    }

    private void Pop(IRRegister reg)
    {
        AddInstruction(Mov, IRFlag.DestRegister | IRFlag.SrcRegister | IRFlag.SrcPointer | _bitnessFlag, reg, R0);
        AddInstruction(Sub, IRFlag.DestRegister | IRFlag.Immediate | _bitnessFlag, R0, PointerSize);
    }
}