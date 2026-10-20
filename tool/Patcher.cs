using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

static class Patcher
{
    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: PclTool <exe> patch"); return 2; }
        string path = args[0];
        var mod = ModuleDefMD.Load(path);
        Console.WriteLine($"Loaded {mod.Name} (MVID {mod.Mvid})");

        int applied = 0;
        applied += PatchRestrictConst(mod);
        applied += PatchLoaderInit(mod);
        applied += PatchCardMinecraft(mod);
        applied += PatchResourceDetail(mod);
        applied += PatchModpackDispatch(mod);

        Console.WriteLine($"Total patches applied: {applied}");

        string outPath = System.IO.Path.ChangeExtension(path, ".restored.exe");
        mod.Write(outPath);
        Console.WriteLine($"WROTE: {outPath}");
        return 0;
    }

    // Runs all restoration transforms against the given module. Returns the number of
    // transforms actually applied (0..5). Never throws on a missed pattern; it just skips.
    public static int PatchAll(ModuleDefMD mod)
    {
        int applied = 0;
        applied += PatchRestrictConst(mod);
        applied += PatchLoaderInit(mod);
        applied += PatchCardMinecraft(mod);
        applied += PatchResourceDetail(mod);
        applied += PatchModpackDispatch(mod);
        return applied;
    }

    // ---- field / method resolution ----------------------------------------------------

    static FieldDef FindField(ModuleDefMD mod, string name)
    {
        foreach (var t in mod.GetTypes())
            foreach (var f in t.Fields)
                if (f.Name == name) return f;
        return null;
    }

    static FieldDef FindField(ModuleDefMD mod, string typeContains, string fieldName)
    {
        foreach (var t in mod.GetTypes())
            if (t.FullName.Contains(typeContains))
                foreach (var f in t.Fields)
                    if (f.Name == fieldName) return f;
        return null;
    }

    static MethodDef FindMethod(ModuleDefMD mod, string typeContains, string methodName)
    {
        return FindMethods(mod, typeContains, methodName).FirstOrDefault();
    }

    static IEnumerable<MethodDef> FindMethods(ModuleDefMD mod, string typeContains, string methodName)
    {
        foreach (var t in mod.GetTypes())
            if (t.FullName.Contains(typeContains))
                foreach (var m in t.Methods)
                    if (m.Name == methodName) yield return m;
    }

    static IEnumerable<MethodDef> AllMethods(ModuleDefMD mod) => mod.GetTypes().SelectMany(t => t.Methods);

    static MethodDef FindDelegateCtor(ModuleDefMD mod, string delegateName)
    {
        foreach (var t in mod.GetTypes())
            if (t.FullName.EndsWith(delegateName))
            {
                var ctor = t.FindMethod(".ctor");
                if (ctor != null) return ctor;
            }
        return null;
    }

    // True if ins is any ldc.i4 form pushing the given value.
    static bool IsLdcI4(Instruction ins, int value)
    {
        var name = ins.OpCode.Code.ToString();
        if (!name.StartsWith("Ldc_I4", StringComparison.Ordinal)) return false;
        try { return ins.GetLdcI4Value() == value; } catch { return false; }
    }

    // ---- 1. restore field constant (metadata truth; code is inlined) ------------------

    static int PatchRestrictConst(ModuleDefMD mod)
    {
        var f = FindField(mod, "RestrictMcDownload");
        if (f == null) { Console.WriteLine("  [field] RestrictMcDownload NOT FOUND"); return 0; }
        Console.WriteLine($"  [field] RestrictMcDownload on {f.DeclaringType.FullName}: IsLiteral={f.IsLiteral} HasConstant={f.HasConstant}");
        if (f.HasConstant)
        {
            f.Constant = new ConstantUser { Type = ElementType.Boolean, Value = (bool)false };
            Console.WriteLine("  [field] RestrictMcDownload constant -> False (metadata truth)");
            return 1;
        }
        return 0;
    }

    // ---- 2. restore MC download list (PageDownloadInstall.LoaderInit) -----------------

    static int PatchLoaderInit(ModuleDefMD mod)
    {
        var dlField = FindField(mod, "DlClientListLoader");
        if (dlField == null) { Console.WriteLine("  [loader] DlClientListLoader field NOT FOUND"); return 0; }
        int n = 0;
        var m = FindMethod(mod, "PageDownloadInstall", "LoaderInit");
        if (m != null && m.HasBody)
        {
            var instrs = m.Body.Instructions;
            int start = -1;
            for (int i = 0; i < instrs.Count; i++)
                if (instrs[i].OpCode.Code == Code.Ldstr && instrs[i].Operand is string s && s == "关闭下载") { start = i; break; }
            if (start < 0) { Console.WriteLine("  [loader] '关闭下载' NOT FOUND"); return 0; }
            int end = start;
            while (end < instrs.Count)
            {
                var ins = instrs[end];
                if (ins.OpCode.Code == Code.Newobj && ins.Operand is IMethod im && im.DeclaringType.Name.Contains("LoaderTask")) break;
                end++;
            }
            if (end >= instrs.Count) { Console.WriteLine("  [loader] LoaderTask ctor NOT FOUND"); return 0; }

            var newList = new List<Instruction>();
            for (int i = 0; i < start; i++) newList.Add(instrs[i]);
            newList.Add(Instruction.Create(OpCodes.Ldsfld, dlField));
            for (int i = end + 1; i < instrs.Count; i++) newList.Add(instrs[i]);

            instrs.Clear();
            foreach (var ins in newList) instrs.Add(ins);
            Console.WriteLine($"  [loader] LoaderInit: failing loader -> ldsfld {dlField.DeclaringType.FullName}::{dlField.Name}");
            n++;
        }
        if (n == 0) Console.WriteLine("  [loader] LoaderInit NOT FOUND");
        return n;
    }

    // ---- 3. restore CardMinecraft on modify page --------------------------------------

    static int PatchCardMinecraft(ModuleDefMD mod)
    {
        foreach (var m in FindMethods(mod, "PageDownloadModify", "LoadInstanceFinished"))
        {
            if (!m.HasBody) continue;
            var instrs = m.Body.Instructions;
            for (int i = 0; i + 2 < instrs.Count; i++)
            {
                var a = instrs[i];
                var b = instrs[i + 1];
                var c = instrs[i + 2];
                if (a.OpCode.Code == Code.Callvirt && a.Operand is IMethod am && am.Name == "get_CardMinecraft" &&
                    IsLdcI4(b, 2) &&
                    c.OpCode.Code == Code.Callvirt && c.Operand is IMethod cm && cm.Name == "set_Visibility")
                {
                    b.OpCode = OpCodes.Ldc_I4_0; // Visible
                    Console.WriteLine($"  [modify] CardMinecraft.Visibility Collapsed(2)->Visible(0) in {m.FullName}");
                    return 1;
                }
            }
        }
        Console.WriteLine("  [modify] CardMinecraft pattern NOT FOUND");
        return 0;
    }

    // ---- 4. restore resource-detail install button for modpacks ------------------------

    static int PatchResourceDetail(ModuleDefMD mod)
    {
        var m = FindMethod(mod, "PageDownloadResourceDetail", "InitializeStackResourceVersions");
        var targetField = FindField(mod, "PageDownloadResourceDetail", "TargetResourceType");
        var installClick = FindMethod(mod, "PageDownloadResourceDetail", "Install_Click");
        var saveClick = FindMethod(mod, "PageDownloadResourceDetail", "Save_Click");
        var itemDelCtor = FindDelegateCtor(mod, "MyListItem/ClickEventHandler");
        var iconDelCtor = FindDelegateCtor(mod, "MyIconButton/ClickEventHandler");
        if (m == null || !m.HasBody || targetField == null || installClick == null || saveClick == null || itemDelCtor == null || iconDelCtor == null)
        {
            Console.WriteLine("  [detail] prerequisite NOT FOUND (method/field/delegates)"); return 0;
        }

        var instrs = m.Body.Instructions;
        int toList = -1;
        for (int i = 0; i < instrs.Count; i++)
            if (instrs[i].OpCode.Code == Code.Callvirt && instrs[i].Operand is IMethod im && im.Name == "ToListItem")
            { toList = i; break; }
        if (toList < 6) { Console.WriteLine("  [detail] ToListItem NOT FOUND"); return 0; }

        // before ToListItem: [..., ldloc V_7 (receiver), <arg build h1> (ldarg,ldftn,newobj), h2, isBad]
        int receiver = toList - 6;
        // receiver instr must be a local load (the resource version) — sanity check
        if (!(instrs[receiver].OpCode.Code == Code.Ldloc || instrs[receiver].OpCode.Code == Code.Ldloc_S))
        { Console.WriteLine("  [detail] unexpected ToListItem arg layout"); return 0; }

        var elseLabel = Instruction.Create(OpCodes.Nop);
        var afterLabel = Instruction.Create(OpCodes.Nop);

        // branch header
        var header = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, targetField),
            Instruction.Create(OpCodes.Ldc_I4, 2),               // ResourceTypes.ModPack
            Instruction.Create(OpCodes.Bne_Un_S, elseLabel),
            // ModPack: h1 = Install_Click (MyListItem/ClickEventHandler), h2 = Save_Click
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldftn, installClick),
            Instruction.Create(OpCodes.Newobj, itemDelCtor),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldftn, saveClick),
            Instruction.Create(OpCodes.Newobj, iconDelCtor),
            Instruction.Create(OpCodes.Ldloc_1),                  // isBad
            Instruction.Create(OpCodes.Br_S, afterLabel),
            elseLabel,
        };

        // else: original arg build (receiver+1 .. toList-1)
        for (int i = receiver + 1; i < toList; i++) header.Add(instrs[i]);
        header.Add(afterLabel);

        var newList = new List<Instruction>();
        for (int i = 0; i <= receiver; i++) newList.Add(instrs[i]);  // keep .Children() + receiver load
        newList.AddRange(header);
        for (int i = toList; i < instrs.Count; i++) newList.Add(instrs[i]);

        instrs.Clear();
        foreach (var ins in newList) instrs.Add(ins);
        Console.WriteLine($"  [detail] InitializeStackResourceVersions: modpack install button restored");
        return 1;
    }

    // ---- 5. restore modpack dispatch (ModModpack.ModpackInstall(File,...)) -------------

    static int PatchModpackDispatch(ModuleDefMD mod)
    {
        MethodDef m = null;
        foreach (var mm in FindMethods(mod, "ModModpack", "ModpackInstall"))
            if (mm.Parameters.Count == 3) { m = mm; break; }
        if (m == null || !m.HasBody) { Console.WriteLine("  [modpack] target method NOT FOUND"); return 0; }
        var body = m.Body;
        var instrs = body.Instructions;

        int t = -1;
        for (int i = 0; i < instrs.Count; i++)
            if (instrs[i].OpCode.Code == Code.Ldstr && instrs[i].Operand is string s && s.StartsWith("为遵守相关法律法规"))
            { t = i; break; }
        if (t < 0 || t + 2 >= instrs.Count || instrs[t + 2].OpCode.Code != Code.Throw)
        { Console.WriteLine("  [modpack] restriction-throw NOT FOUND"); return 0; }

        if (body.Variables.Count < 5) { Console.WriteLine("  [modpack] no PackType local"); return 0; }
        Local packType = body.Variables[4]; // V_4

        var ipCurse = FindMethod(mod, "ModModpack", "InstallPackCurseForge");
        var ipHMCL = FindMethod(mod, "ModModpack", "InstallPackHMCL");
        var ipMMC = FindMethod(mod, "ModModpack", "InstallPackMMC");
        var ipMCBBS = FindMethod(mod, "ModModpack", "InstallPackMCBBS");
        var ipModrinth = FindMethod(mod, "ModModpack", "InstallPackModrinth");
        var ipLauncher = FindMethod(mod, "ModModpack", "InstallPackLauncherPack");
        var ipCompress = FindMethod(mod, "ModModpack", "InstallPackCompress");
        if (ipCurse == null || ipHMCL == null || ipMMC == null || ipMCBBS == null || ipModrinth == null || ipLauncher == null || ipCompress == null)
        { Console.WriteLine("  [modpack] InstallPack* method NOT FOUND"); return 0; }

        var caseCurse = BuildCaseFn(body, ipCurse);
        var caseHMCL = BuildCaseFn(body, ipHMCL);
        var caseMMC = BuildCaseFn(body, ipMMC);
        var caseMCBBS = BuildCaseFn(body, ipMCBBS);
        var caseModrinth = BuildCaseFn(body, ipModrinth);
        var caseLauncher = BuildCaseFn(body, ipLauncher);
        var caseCompress = BuildCaseFn(body, ipCompress);

        var targets = new Instruction[10];
        targets[0] = caseCurse.Start;
        targets[1] = caseHMCL.Start;
        targets[2] = caseMMC.Start;
        targets[3] = caseMCBBS.Start;
        targets[4] = caseModrinth.Start;
        for (int i = 5; i <= 8; i++) targets[i] = caseCompress.Start;   // fall to Compress
        targets[9] = caseLauncher.Start;

        // Repurpose instrs[t] (= the ldstr) as the dispatch entry so all the leave-targets
        // in the parse section remain valid. instrs[t+1] (newobj) becomes the switch; the
        // throw instrs[t+2] is dropped. This keeps branch references consistent.
        var dispatchEntry = instrs[t];
        var switchIns = instrs[t + 1];
        var throwIns = instrs[t + 2];

        var middle = new List<Instruction>();
        middle.AddRange(caseCompress.Block);   // fall-through default = Compress (= Case Else)
        middle.AddRange(caseCurse.Block);
        middle.AddRange(caseHMCL.Block);
        middle.AddRange(caseMMC.Block);
        middle.AddRange(caseMCBBS.Block);
        middle.AddRange(caseModrinth.Block);
        middle.AddRange(caseLauncher.Block);

        var newList = new List<Instruction>();
        for (int i = 0; i < t; i++) newList.Add(instrs[i]);                 // parse + leave targets kept
        dispatchEntry.OpCode = OpCodes.Ldloc; dispatchEntry.Operand = packType;
        newList.Add(dispatchEntry);
        switchIns.OpCode = OpCodes.Switch; switchIns.Operand = targets;
        newList.Add(switchIns);
        newList.AddRange(middle);
        for (int i = t + 3; i < instrs.Count; i++) newList.Add(instrs[i]); // keep finally handler

        instrs.Clear();
        foreach (var ins in newList) instrs.Add(ins);
        Console.WriteLine($"  [modpack] ModpackInstall(File) dispatch restored");
        return 1;
    }

    // Builds a dispatch case block that calls one InstallPack* with the right args and returns.
    // The InstallPack* family shares the argument layout File, Archive, [Base], [Instance], [Logo];
    // the exact set is derived from the target method's own parameter count, so e.g. the 2-arg
    // InstallPackCompress only pushes (File, Archive) and leaves a clean stack on return.
    static CaseBlock BuildCaseFn(CilBody body, IMethod fn)
    {
        int n = fn.MethodSig.Params.Count;
        var list = new List<Instruction>();
        var start = Instruction.Create(OpCodes.Ldarg_0);                    // File
        list.Add(start);
        list.Add(Instruction.Create(OpCodes.Ldloc, body.Variables[1]));     // Archive
        if (n >= 3) list.Add(Instruction.Create(OpCodes.Ldloc, body.Variables[2])); // ArchiveBaseFolder
        if (n >= 4) list.Add(Instruction.Create(OpCodes.Ldarg_1));          // InstanceName
        if (n >= 5) list.Add(Instruction.Create(OpCodes.Ldarg_2));          // Logo
        list.Add(Instruction.Create(OpCodes.Call, fn));
        list.Add(Instruction.Create(OpCodes.Ret));
        return new CaseBlock { Start = start, Block = list };
    }

    class CaseBlock { public Instruction Start; public List<Instruction> Block; }
}
