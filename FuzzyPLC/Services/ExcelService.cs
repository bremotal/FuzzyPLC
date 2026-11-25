using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Newtonsoft.Json;
using FuzzyPLC.Models;

namespace FuzzyPLC.Services
{
    public static class ExcelService
    {
        static XLColor H1  => XLColor.FromHtml("#1E1E1E");
        static XLColor GRN => XLColor.FromHtml("#E2EFDA");
        static XLColor YEL => XLColor.FromHtml("#FFF2CC");
        static XLColor BLU => XLColor.FromHtml("#D6E4F7");
        static XLColor RED => XLColor.FromHtml("#FFE0E0");
        static XLColor ORG => XLColor.FromHtml("#FCE4D6");
        static XLColor PRP => XLColor.FromHtml("#EAD1DC");

        public static bool Export(string path, FuzzyProject proj, CalcResult res)
        {
            try {
                using var wb = new XLWorkbook();
                Sheet_Config    (wb, proj);
                Sheet_CalcFuzzy (wb, proj, res);
                Sheet_Linearizers(wb, res.Linearizers);
                Sheet_CurvaFinal(wb, proj, res);
                Sheet_ExportPLC (wb, res);
                wb.SaveAs(path);
                return true;
            } catch (Exception ex) {
                System.Windows.MessageBox.Show($"Erro Excel:\n{ex.Message}","Erro",
                    System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        static void Sheet_Config(IXLWorkbook wb, FuzzyProject proj)
        {
            var ws=wb.Worksheets.Add("CONFIG");
            Title(ws,1,$"CONFIGURACAO - {proj.Name}",7);
            ws.Cell(2,1).Value=proj.Description; Mg(ws,2,1,2,7);

            Title(ws,4,"PARAMETROS MPC",7);
            Hdr(ws,5,new[]{"PV_MIN","PV_MAX","SP","CMD_MIN","CMD_MAX","CMD_MID","N Pontos"});
            var m=proj.Mpc;
            Row(ws,6,BLU,m.PvMin,m.PvMax,m.Sp,m.CmdMin,m.CmdMax,m.CmdMid,m.NPoints);
            Hdr(ws,8,new[]{"Alpha","Tau","K","Tipo de Acao","","",""});
            Row(ws,9,BLU,m.Alpha,m.Tau,m.K,m.ActionLabel,"","","");

            int r=12;
            Title(ws,r,"VARIAVEIS AUXILIARES",7);
            Hdr(ws,r+1,new[]{"Nome","Unidade","Min","Ideal","Max","Peso (pi)","Comportamento"});
            for(int i=0;i<proj.Variables.Count;i++){
                var v=proj.Variables[i];
                Row(ws,r+2+i,v.IsMain?GRN:i%2==0?BLU:XLColor.White,
                    v.Name+(v.IsMain?" [PRINCIPAL]":""),v.Unit,v.Min,v.Ideal,v.Max,v.Weight,v.BehaviorLabel);
            }
            for(int c=1;c<=7;c++) ws.Column(c).Width=18;
        }

        static void Sheet_CalcFuzzy(IXLWorkbook wb, FuzzyProject proj, CalcResult res)
        {
            var ws=wb.Worksheets.Add("CALC_FUZZY");
            Title(ws,1,"CALCULO FUZZY - Severidade e Ajuste MPC",8);
            ws.Cell(3,1).Value="FORMULAS:"; ws.Cell(3,1).Style.Font.Bold=true;
            var fms=new[]{"Sec.6: mu_i = |valor - ideal| / (max - min)   [0 <= mu <= 1]",
                          "Sec.7: S    = Sum(mu_i * p_i) / Sum(p_i)       Severidade Global",
                          "Sec.8: tau_ef = tau * (1 + S)                  Tau efetivo",
                          "Sec.8: alpha_ef = alpha / (1 + k * tau_ef)     Alpha efetivo"};
            for(int i=0;i<fms.Length;i++){
                ws.Cell(4+i,1).Value=fms[i];
                ws.Cell(4+i,1).Style.Font.FontName="Courier New";
                ws.Cell(4+i,1).Style.Font.FontColor=XLColor.FromHtml("#E8640C");
                Mg(ws,4+i,1,4+i,8);}

            Title(ws,10,"PERTINENCIAS CALCULADAS",8);
            Hdr(ws,11,new[]{"Variavel","Valor","Min","Ideal","Max","mu_i","Peso(pi)","mu*p"});
            for(int i=0;i<res.AuxStates.Count;i++){
                var s=res.AuxStates[i];
                var v=proj.Variables.Where(x=>!x.IsMain).ElementAtOrDefault(i);
                int row=12+i; var fill=i%2==0?BLU:XLColor.White;
                ws.Cell(row,1).Value=s.Name; ws.Cell(row,2).Value=s.Value; Fmt(ws,row,2,"0.000");
                ws.Cell(row,3).Value=v?.Min??0; ws.Cell(row,4).Value=v?.Ideal??0; ws.Cell(row,5).Value=v?.Max??0;
                ws.Cell(row,6).Value=s.Mu; Fmt(ws,row,6,"0.0000");
                ws.Cell(row,7).Value=s.Weight; Fmt(ws,row,7,"0.00");
                ws.Cell(row,8).Value=s.WxMu; Fmt(ws,row,8,"0.0000");
                for(int c=1;c<=8;c++){ws.Cell(row,c).Style.Fill.BackgroundColor=fill;
                    ws.Cell(row,c).Style.Border.OutsideBorder=XLBorderStyleValues.Thin;
                    ws.Cell(row,c).Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;}}

            int rr=12+res.AuxStates.Count+2;
            Title(ws,rr,"RESULTADOS",8);
            var kv=new[]{
                ($"Severidade S = {res.Severity:F4}",""),
                ($"tau_ef = {res.TauEf:F4}","tau_ef = tau x (1 + S)"),
                ($"alpha_ef = {res.AlphaEf:F4}","alpha_ef = alpha / (1 + k x tau_ef)"),
                ($"A = {res.A:F3}","A = (CMDmax - CMDmin) / 2"),};
            for(int i=0;i<kv.Length;i++){
                ws.Cell(rr+1+i,1).Value=kv[i].Item1; ws.Cell(rr+1+i,1).Style.Font.Bold=true;
                ws.Cell(rr+1+i,1).Style.Font.FontColor=XLColor.FromHtml("#E8640C");
                ws.Cell(rr+1+i,2).Value=kv[i].Item2; Mg(ws,rr+1+i,2,rr+1+i,8);}
            for(int c=1;c<=8;c++) ws.Column(c).Width=20;
        }

        static void Sheet_Linearizers(IXLWorkbook wb, List<Linearizer> lins)
        {
            var pal=new[]{BLU,GRN,YEL,ORG,PRP};
            int ci=0;
            foreach(var lin in lins){
                if(lin.Type==LinearizerType.MpcCurve) continue;
                string sn=lin.Name.Length>28?lin.Name[..28]:lin.Name;
                var ws=wb.Worksheets.Add(sn);
                var fill=pal[ci++%pal.Length];
                Title(ws,1,$"{lin.Name} ({lin.TypeLabel})",5);
                ws.Cell(2,1).Value=$"Pontos: {lin.Count} | {lin.Notes}";
                ws.Cell(2,1).Style.Font.Italic=true; Mg(ws,2,1,2,5);
                // Nota sobre limites industriais
                ws.Cell(3,1).Value="LIMITE INDUSTRIAL: Saida sempre em [0.0, 1.0] — 0%=fechado/parado, 100%=aberto/maximo";
                ws.Cell(3,1).Style.Font.Bold=true;
                ws.Cell(3,1).Style.Font.FontColor=XLColor.FromHtml("#E8640C");
                Mg(ws,3,1,3,5);
                Hdr(ws,5,new[]{"Pt","Entrada","Saida [0.0-1.0]","Variacao %","Interpretacao"});
                for(int i=0;i<lin.Count;i++){
                    int row=i+6; double inp=lin.InputPts[i],out_=lin.OutputPts[i],pct=(out_-0.5)*100;
                    string interp=out_>0.7?"Alto - mais acao":out_<0.3?"Baixo - menos acao":"Medio - nominal";
                    XLColor rf=out_>0.7?GRN:out_<0.3?RED:XLColor.White;
                    ws.Cell(row,1).Value=i+1; ws.Cell(row,2).Value=inp; Fmt(ws,row,2,"0.000");
                    ws.Cell(row,3).Value=out_; Fmt(ws,row,3,"0.000"); ws.Cell(row,4).Value=pct; Fmt(ws,row,4,"+0.0;-0.0");
                    ws.Cell(row,5).Value=interp;
                    for(int c=1;c<=5;c++){ws.Cell(row,c).Style.Fill.BackgroundColor=i%2==0?rf:XLColor.White;
                        ws.Cell(row,c).Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;
                        ws.Cell(row,c).Style.Border.OutsideBorder=XLBorderStyleValues.Thin;}}
                ws.Column(1).Width=6; ws.Column(2).Width=16; ws.Column(3).Width=18; ws.Column(4).Width=14; ws.Column(5).Width=24;
            }
        }

        static void Sheet_CurvaFinal(IXLWorkbook wb, FuzzyProject proj, CalcResult res)
        {
            var ws=wb.Worksheets.Add("CURVA_FINAL"); var m=proj.Mpc;
            Title(ws,1,$"CURVA MPC FINAL | SP={m.Sp} | PV:[{m.PvMin}-{m.PvMax}] | CMD:[{m.CmdMin}-{m.CmdMax}]",8);
            ws.Cell(2,1).Value=$"S={res.Severity:F4} | tauEf={res.TauEf:F4} | alphaEf={res.AlphaEf:F4} | A={res.A:F3} | SAIDA: [{m.CmdMin}, {m.CmdMax}]";
            ws.Cell(2,1).Style.Font.Bold=true; ws.Cell(2,1).Style.Font.FontColor=XLColor.FromHtml("#E8640C"); Mg(ws,2,1,2,8);
            Hdr(ws,4,new[]{"Pt","PV","z=(PV-SP)/Range","CMD","Zona","","PV(PLC)","CMD(PLC)"});
            ws.Cell(4,7).Style.Fill.BackgroundColor=XLColor.FromHtml("#1B5E20"); ws.Cell(4,7).Style.Font.FontColor=XLColor.White;
            ws.Cell(4,8).Style.Fill.BackgroundColor=XLColor.FromHtml("#1B5E20"); ws.Cell(4,8).Style.Font.FontColor=XLColor.White;
            foreach(var pt in res.MpcCurve){
                int row=pt.Index+4; XLColor fill=pt.Zone=="IDEAL"?GRN:pt.Zone=="ABAIXO"?YEL:RED;
                ws.Cell(row,1).Value=pt.Index; ws.Cell(row,2).Value=pt.Pv; Fmt(ws,row,2,"0.000");
                ws.Cell(row,3).Value=pt.Z; Fmt(ws,row,3,"0.0000"); ws.Cell(row,4).Value=pt.Cmd; Fmt(ws,row,4,"0.000");
                ws.Cell(row,5).Value=pt.Zone;
                ws.Cell(row,7).Value=pt.Pv; Fmt(ws,row,7,"0.000"); ws.Cell(row,8).Value=pt.Cmd; Fmt(ws,row,8,"0.000");
                for(int c=1;c<=5;c++){ws.Cell(row,c).Style.Fill.BackgroundColor=pt.Index%2==0?fill:XLColor.White;
                    ws.Cell(row,c).Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;
                    ws.Cell(row,c).Style.Border.OutsideBorder=XLBorderStyleValues.Thin;}
                ws.Cell(row,7).Style.Fill.BackgroundColor=fill; ws.Cell(row,8).Style.Fill.BackgroundColor=fill;
                ws.Cell(row,7).Style.Border.OutsideBorder=XLBorderStyleValues.Thin;
                ws.Cell(row,8).Style.Border.OutsideBorder=XLBorderStyleValues.Thin;
                ws.Cell(row,7).Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;
                ws.Cell(row,8).Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;}
            for(int c=1;c<=8;c++) ws.Column(c).Width=16;
        }

        static void Sheet_ExportPLC(IXLWorkbook wb, CalcResult res)
        {
            var ws=wb.Worksheets.Add("EXPORT_PLC");
            Title(ws,1,"EXPORTACAO PARA PLC - Siemens | Rockwell | PLC Legado",6);
            int r=3;
            ws.Cell(r,1).Value="CSV - CURVA MPC (PV;CMD) - Padrao Industrial [CmdMin, CmdMax]:";
            ws.Cell(r,1).Style.Font.Bold=true; r++;
            ws.Cell(r,1).Value="PV;CMD"; ws.Cell(r,1).Style.Font.Bold=true; r++;
            foreach(var pt in res.MpcCurve){
                ws.Cell(r,1).Value=$"{pt.Pv:F3};{pt.Cmd:F3}".Replace('.',',');
                ws.Cell(r,1).Style.Font.FontName="Courier New"; r++;}
            r+=2;
            ws.Cell(r,1).Value="CODIGO IEC 61131-3 - Structured Text:"; ws.Cell(r,1).Style.Font.Bold=true; r++;
            foreach(var lin in res.Linearizers){
                ws.Cell(r,1).Value=lin.GeneratePlcCode();
                ws.Cell(r,1).Style.Font.FontName="Courier New";
                ws.Cell(r,1).Style.Font.FontSize=9;
                ws.Cell(r,1).Style.Alignment.WrapText=true;
                r+=lin.Count+8;}
            ws.Column(1).Width=100;
        }

        static void Title(IXLWorksheet ws,int row,string t,int cols,string hex="#1E1E1E"){
            Mg(ws,row,1,row,cols); var c=ws.Cell(row,1); c.Value=t;
            c.Style.Font.Bold=true; c.Style.Font.FontSize=12;
            c.Style.Font.FontColor=XLColor.FromHtml(hex);
            c.Style.Fill.BackgroundColor=XLColor.FromHtml("#F5F5F5");
            c.Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;
            ws.Row(row).Height=22;}

        static void Hdr(IXLWorksheet ws,int row,string[] hs){
            for(int i=0;i<hs.Length;i++){var c=ws.Cell(row,i+1); c.Value=hs[i];
                c.Style.Font.Bold=true; c.Style.Font.FontColor=XLColor.White;
                c.Style.Fill.BackgroundColor=XLColor.FromHtml("#E8640C");
                c.Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;
                c.Style.Border.OutsideBorder=XLBorderStyleValues.Thin;}
            ws.Row(row).Height=20;}

        static void Row(IXLWorksheet ws,int row,XLColor fill,params object[] vs){
            for(int i=0;i<vs.Length;i++){var c=ws.Cell(row,i+1);
                if(vs[i] is double d) c.Value=d;
                else if(vs[i] is int n) c.Value=n;
                else if(vs[i] is bool b) c.Value=b;
                else if(vs[i] is string s) c.Value=s;
                else if(vs[i]!=null) c.Value=vs[i].ToString();
                c.Style.Fill.BackgroundColor=fill;
                c.Style.Alignment.Horizontal=i==0?XLAlignmentHorizontalValues.Left:XLAlignmentHorizontalValues.Center;
                c.Style.Border.OutsideBorder=XLBorderStyleValues.Thin;
                if(vs[i] is double) c.Style.NumberFormat.Format="0.000";}}

        static void Fmt(IXLWorksheet ws,int r,int c,string fmt)
            =>ws.Cell(r,c).Style.NumberFormat.Format=fmt;
        static void Mg(IXLWorksheet ws,int r1,int c1,int r2,int c2)
            =>ws.Range(r1,c1,r2,c2).Merge();

        public static void SaveCsvMpc(CalcResult res, string path)
        {
            var lines=new List<string>{"PV;CMD"};
            foreach(var pt in res.MpcCurve)
                lines.Add($"{pt.Pv:F3};{pt.Cmd:F3}".Replace('.',','));
            File.WriteAllLines(path,lines,System.Text.Encoding.UTF8);
        }

        public static void SaveCsvLinearizers(CalcResult res, string dir)
        {
            Directory.CreateDirectory(dir);
            foreach(var lin in res.Linearizers){
                var lines=new List<string>{"Entrada;Saida"};
                for(int i=0;i<lin.Count;i++)
                    lines.Add($"{lin.InputPts[i]:F3};{lin.OutputPts[i]:F3}".Replace('.',','));
                File.WriteAllLines(Path.Combine(dir,$"{lin.Name}.csv"),lines,System.Text.Encoding.UTF8);}
        }
    }

    public class ConfigManager
    {
        public string Dir { get; }
        public ConfigManager(string dir="outputs"){ Dir=dir; Directory.CreateDirectory(dir); }
        public string? Save(FuzzyProject p, string? path=null){
            path??=Path.Combine(Dir,"config_backup.json");
            try{ p.SavedAt=DateTime.Now;
                File.WriteAllText(path,JsonConvert.SerializeObject(p,Formatting.Indented),System.Text.Encoding.UTF8);
                return path;}
            catch(Exception ex){ Msg($"Erro ao salvar:\n{ex.Message}"); return null;}}
        public FuzzyProject? Load(string? path=null){
            path??=Path.Combine(Dir,"config_backup.json");
            if(!File.Exists(path)) return null;
            try{ return JsonConvert.DeserializeObject<FuzzyProject>(File.ReadAllText(path,System.Text.Encoding.UTF8));}
            catch(Exception ex){ Msg($"Erro ao carregar:\n{ex.Message}"); return null;}}
        public void SaveReport(string txt,string? path=null){
            path??=Path.Combine(Dir,"relatorio.txt");
            try{File.WriteAllText(path,txt,System.Text.Encoding.UTF8);}catch{}}
        static void Msg(string m)=>System.Windows.MessageBox.Show(m,"Erro",
            System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Error);
    }
}
