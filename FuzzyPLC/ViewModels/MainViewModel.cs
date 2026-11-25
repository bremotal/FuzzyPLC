using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using FuzzyPLC.Core;
using FuzzyPLC.Models;
using static FuzzyPLC.Models.ActionType;
using FuzzyPLC.Services;
using Microsoft.Win32;

namespace FuzzyPLC.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        readonly ConfigManager _cfg = new("outputs");

        private FuzzyProject _proj  = new FuzzyProject(); // Inicia VAZIO
        private CalcResult?  _res   = null;
        private string _status = "Pronto. Configure as variaveis e clique em CALCULAR.";
        private string _clock  = DateTime.Now.ToString("HH:mm:ss");
        private int    _tab    = 0;
        private FuzzyVariable? _selVar;
        private Linearizer?    _selLin;

        public FuzzyProject   Project   { get=>_proj;   set{_proj=value;   OnP(); OnP(nameof(HasVars));} }
        public CalcResult?    Result    { get=>_res;    set{_res=value;    OnP(); OnP(nameof(HasResult)); LoadRows(); LoadSummary();} }
        public string         Status    { get=>_status; set{_status=value; OnP();} }
        public string         Clock     { get=>_clock;  set{_clock=value;  OnP();} }
        public int            ActiveTab { get=>_tab;    set{_tab=value;    OnP();} }
        public bool           HasVars   => Project.Variables.Count>0;
        public bool           HasResult => _res!=null;

        public FuzzyVariable? SelectedVar { get=>_selVar; set{_selVar=value; OnP(); OnP(nameof(HasSel));} }
        public Linearizer? SelectedLin {
            get=>_selLin;
            set{_selLin=value; OnP();
                if(value!=null){LinRows.Clear();
                    for(int i=0;i<value.Count;i++)
                        LinRows.Add(new LinearizerRow{Pt=i+1,Input=value.InputPts[i],Output=value.OutputPts[i]});}}}
        public bool HasSel => _selVar!=null;

        public ObservableCollection<MpcPoint>      MpcRows    {get;}=new();
        public ObservableCollection<AuxVarState>   AuxRows    {get;}=new();
        public ObservableCollection<Linearizer>    Linearizers{get;}=new();
        public ObservableCollection<LinearizerRow> LinRows    {get;}=new();
        public ObservableCollection<string>        PlcCode    {get;}=new();

        private double _sev,_tef,_aef,_amp;
        public double Severity  {get=>_sev; set{_sev=value; OnP();}}
        public double TauEf     {get=>_tef; set{_tef=value; OnP();}}
        public double AlphaEf   {get=>_aef; set{_aef=value; OnP();}}
        public double Amplitude {get=>_amp; set{_amp=value; OnP();}}

        public FuzzyBehavior[] Behaviors    => new[]{FuzzyBehavior.Otimo, FuzzyBehavior.Direto, FuzzyBehavior.Inverso};
        public ActionType[]    ActionTypes  => new[]{ActionType.Direta, ActionType.Reversa};

        public MainViewModel()
        {
            var ck=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
            ck.Tick+=(_,_)=>Clock=DateTime.Now.ToString("HH:mm:ss");
            ck.Start();
            var saved=_cfg.Load();
            if(saved!=null){ Project=saved; Status="Configuracao anterior carregada automaticamente."; }
        }

        public void AddVariable()
        {
            int n=Project.Variables.Count+1;
            var v=new FuzzyVariable{Name=$"Var{n}",Unit="",Min=0,Max=100,Ideal=50,
                Behavior=FuzzyBehavior.Otimo,NPoints=10,Weight=1.0,
                IsMain=Project.Variables.Count==0};
            Project.Variables.Add(v); SelectedVar=v;
            OnP(nameof(HasVars)); Status=$"Variavel '{v.Name}' adicionada.";
        }

        public void RemoveVariable(FuzzyVariable v)
        {
            if(MessageBox.Show($"Remover '{v.Name}'?","Confirmar",
                MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) return;
            Project.Variables.Remove(v); if(SelectedVar==v) SelectedVar=null;
            OnP(nameof(HasVars));
        }

        public void SetAsMain(FuzzyVariable v)
        {
            foreach(var x in Project.Variables) x.IsMain=false;
            v.IsMain=true; Status=$"'{v.Name}' definida como variavel principal.";
        }

        public void Calculate()
        {
            if(!Project.Variables.Any()){
                MessageBox.Show("Configure ao menos uma variavel!","Aviso",
                    MessageBoxButton.OK,MessageBoxImage.Warning); return;}
            var res=MpcEngine.Calculate(Project);
            Result=res;
            Linearizers.Clear(); foreach(var l in res.Linearizers) Linearizers.Add(l);
            if(Linearizers.Any()) SelectedLin=Linearizers[0];
            PlcCode.Clear();
            foreach(var lin in res.Linearizers)
                foreach(var ln in lin.GeneratePlcCode().Split('\n')) PlcCode.Add(ln);
            string xlsx=Path.Combine(_cfg.Dir,"tabelas_fuzzy_mpc.xlsx");
            if(ExcelService.Export(xlsx,Project,res))
                Status=$"Calculado! {res.Linearizers.Count} linearizadores + curva MPC. Excel: {xlsx}";
            else Status="Calculado! (Falha ao salvar Excel)";
            ExcelService.SaveCsvMpc(res,Path.Combine(_cfg.Dir,"curva_mpc.csv"));
            ExcelService.SaveCsvLinearizers(res,Path.Combine(_cfg.Dir,"csv"));
            ActiveTab=2;
        }

        void LoadRows()
        {
            MpcRows.Clear(); AuxRows.Clear(); if(_res==null) return;
            foreach(var pt in _res.MpcCurve) MpcRows.Add(pt);
            foreach(var s in _res.AuxStates) AuxRows.Add(s);
        }
        void LoadSummary()
        {
            if(_res==null) return;
            Severity=_res.Severity; TauEf=_res.TauEf; AlphaEf=_res.AlphaEf; Amplitude=_res.A;
        }

        public void ExportExcel()
        {
            if(_res==null){Calculate(); return;}
            var dlg=new SaveFileDialog{Filter="Excel|*.xlsx",
                FileName=$"Fuzzy_MPC_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"};
            if(dlg.ShowDialog()!=true) return;
            if(ExcelService.Export(dlg.FileName,Project,_res))
                Status=$"Excel exportado: {Path.GetFileName(dlg.FileName)}";
        }

        public void ExportCsv()
        {
            if(_res==null){Calculate(); return;}
            var dlg=new SaveFileDialog{Filter="CSV|*.csv",FileName="curva_mpc.csv"};
            if(dlg.ShowDialog()!=true) return;
            ExcelService.SaveCsvMpc(_res,dlg.FileName);
            Status=$"CSV exportado: {Path.GetFileName(dlg.FileName)}";
            MessageBox.Show($"CSV salvo!\nImporte no PLC via bloco de linearizacao.","Exportado",
                MessageBoxButton.OK,MessageBoxImage.Information);
        }

        public void NewProject()
        {
            if(MessageBox.Show("Criar novo projeto?","Confirmar",
                MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
            Project=new FuzzyProject(); Result=null;
            Linearizers.Clear(); MpcRows.Clear(); SelectedVar=null;
            Status="Novo projeto criado."; ActiveTab=0;
        }
        public void LoadExample(){ Project=FuzzyProject.CreatePhExample(); Result=null;
            Linearizers.Clear(); MpcRows.Clear(); SelectedVar=null; Status="Exemplo pH carregado."; }
        // Salva no local padrão (outputs/config_backup.json)
        public void SaveProject(){
            var p=_cfg.Save(Project);
            Status=p!=null?$"✅ Salvo: {Path.GetFileName(p)}":"❌ Falha ao salvar.";}

        // Salva com nome e pasta escolhidos pelo usuário
        public void SaveProjectAs(){
            var dlg=new SaveFileDialog{
                Filter   = "Projeto FuzzyMPC (*.json)|*.json",
                FileName = $"{Project.Name.Replace(" ","_")}.json",
                Title    = "Salvar Projeto Como"
            };
            if(dlg.ShowDialog()!=true) return;
            _cfg.Save(Project,dlg.FileName);
            Status=$"✅ Projeto salvo: {dlg.FileName}";
        }

        // Exporta projeto completo como ZIP (JSON + Excel + CSVs)
        public void ExportProjectZip(){
            if(_res==null){
                MessageBox.Show("Calcule o MPC primeiro para incluir os resultados no ZIP.",
                    "Aviso",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }
            var dlg=new SaveFileDialog{
                Filter   = "Arquivo ZIP (*.zip)|*.zip",
                FileName = $"{Project.Name.Replace(" ","_")}_{DateTime.Now:yyyyMMdd_HHmm}.zip",
                Title    = "Exportar Projeto como ZIP"
            };
            if(dlg.ShowDialog()!=true) return;

            try{
                string tmpDir = Path.Combine(Path.GetTempPath(), $"FuzzyExport_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tmpDir);

                // 1. Salva JSON do projeto
                string projName = Project.Name.Replace(" ","_");
                _cfg.Save(Project, Path.Combine(tmpDir, $"{projName}.json"));

                // 2. Exporta Excel
                ExcelService.Export(
                    Path.Combine(tmpDir, $"{projName}_tabelas.xlsx"),
                    Project, _res);

                // 3. Exporta CSVs
                ExcelService.SaveCsvMpc(_res,
                    Path.Combine(tmpDir, $"{projName}_curvaMPC.csv"));
                ExcelService.SaveCsvLinearizers(_res,
                    Path.Combine(tmpDir, "csv"));

                // 4. Compacta tudo num ZIP
                if(File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                System.IO.Compression.ZipFile.CreateFromDirectory(tmpDir, dlg.FileName);

                // 5. Limpa pasta temp
                Directory.Delete(tmpDir, true);

                Status = $"✅ ZIP exportado: {Path.GetFileName(dlg.FileName)}";
                string msg = "Projeto exportado com sucesso!\n\n" +
                             $"Arquivo: {dlg.FileName}\n\n" +
                             "Conteudo:\n" +
                             $"  - {projName}.json\n" +
                             $"  - {projName}_tabelas.xlsx\n" +
                             $"  - {projName}_curvaMPC.csv\n" +
                             "  - csv\\ (CSVs por linearizador)";
                MessageBox.Show(msg, "Exportado!",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch(Exception ex){
                Status = $"❌ Erro ao exportar: {ex.Message}";
                MessageBox.Show("Erro ao exportar ZIP:\n" + ex.Message, "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        public void LoadProject(){
            var dlg=new OpenFileDialog{Filter="JSON|*.json"};
            if(dlg.ShowDialog()!=true) return;
            var p=_cfg.Load(dlg.FileName);
            if(p!=null){Project=p; Result=null; Linearizers.Clear(); MpcRows.Clear();
                Status=$"Carregado: {p.Name}";}}

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnP([CallerMemberName] string? n=null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
