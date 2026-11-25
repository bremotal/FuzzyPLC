using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace FuzzyPLC.Models
{
    // ══════════════════════════════════════════════════════════════
    // TIPO DE AÇÃO — equivalente ao PID industrial
    //
    // ATENÇÃO: A nomenclatura varia por fabricante!
    // Este sistema usa a CONVENÇÃO ISA/ANSI:
    //
    //   Direta : PV sobe → CMD sobe   (sem inversão da curva)
    //            Quando usar: processo onde PV alto requer MAIS ação
    //            Ex: resfriamento — temperatura alta → abre mais válvula fria
    //            Ex: álcool alto → AUMENTA refluxo
    //
    //   Reversa: PV sobe → CMD desce  (curva espelhada)
    //            Quando usar: processo onde PV alto requer MENOS ação
    //            Ex: pH alto → fecha válvula de ácido
    //            Ex: pressão alta → fecha entrada de vapor
    //
    // DICA: Se seu PID físico está em "Reverso" e o comportamento
    // parece invertido aqui, tente "Direta" — seu fabricante pode
    // usar convenção oposta à ISA.
    // ══════════════════════════════════════════════════════════════
    public enum ActionType
    {
        Direta,   // PV↑ → CMD↑  (sem inversão — convenção ISA Direta)
        Reversa   // PV↑ → CMD↓  (com inversão — convenção ISA Reversa)
    }

    public enum FuzzyBehavior { Otimo, Direto, Inverso }

    public class TimeResponseConfig : INotifyPropertyChanged
    {
        private bool _en=false; private double _val=60; private double _int=0.8;
        public bool   Enabled   { get=>_en;  set{_en=value;  OnP();} }
        public double Value     { get=>_val; set{_val=value; OnP(); OnP(nameof(Category));} }
        public double Intensity { get=>_int; set{_int=Math.Max(0.1,Math.Min(1.0,value)); OnP();} }
        [JsonIgnore] public string Category => Value<=60?"Rapido (<=1 min)":Value<=180?"Medio (1-3 min)":"Lento (>3 min)";
        public event PropertyChangedEventHandler? PropertyChanged;
        void OnP([CallerMemberName] string? n=null) => PropertyChanged?.Invoke(this, new(n));
    }

    public class FuzzyVariable : INotifyPropertyChanged
    {
        private string _name=""; private string _unit="";
        private double _min=0,_max=100,_ideal=50,_weight=1.0;
        private FuzzyBehavior _beh=FuzzyBehavior.Otimo;
        private int _nPts=10; private bool _isMain=false;

        public string        Name     { get=>_name;   set{_name=value;   OnP();} }
        public string        Unit     { get=>_unit;   set{_unit=value;   OnP();} }
        public double        Min      { get=>_min;    set{_min=value;    OnP();} }
        public double        Max      { get=>_max;    set{_max=value;    OnP();} }
        public double        Ideal    { get=>_ideal;  set{_ideal=value;  OnP();} }
        public FuzzyBehavior Behavior { get=>_beh;    set{_beh=value;    OnP(); OnP(nameof(BehaviorLabel));} }
        public int           NPoints  { get=>_nPts;   set{_nPts=Math.Max(1,Math.Min(200,value)); OnP();} }
        public bool          IsMain   { get=>_isMain; set{_isMain=value; OnP(); OnP(nameof(RoleLabel));} }
        public double        Weight   { get=>_weight; set{_weight=Math.Max(0,Math.Min(10,value)); OnP();} }

        // ── Zona morta e Histerese por variável ──────────────────────
        private double _deadband = 5.0;
        private double _hyst     = 2.0;

        public double Deadband
        {
            get => _deadband;
            set { _deadband = Math.Max(0, Math.Min(20, value)); OnP(); OnP(nameof(DeadbandLabel)); }
        }
        public double Hysteresis
        {
            get => _hyst;
            set { _hyst = Math.Max(0, Math.Min(10, value)); OnP(); OnP(nameof(HystLabel)); }
        }
        [Newtonsoft.Json.JsonIgnore]
        public string DeadbandLabel
        {
            get { double h=(Max-Min)*(_deadband/100.0)/2.0; return $"±{h:F3} {Unit} em torno do SP"; }
        }
        [Newtonsoft.Json.JsonIgnore]
        public string HystLabel
        {
            get { double db=(Max-Min)*(_deadband/100.0)/2.0; double h=(Max-Min)*(_hyst/100.0)/2.0;
                  return $"Sai zona morta em ±{(db+h):F3} {Unit} do SP"; }
        }

        public TimeResponseConfig TimeResponse { get;set; } = new();

        [JsonIgnore] public string BehaviorLabel => Behavior switch {
            FuzzyBehavior.Otimo   => "Otimo - ambos os lados",
            FuzzyBehavior.Direto  => "Direto - abaixo do ideal",
            FuzzyBehavior.Inverso => "Inverso - acima do ideal", _=>"" };
        [JsonIgnore] public string RoleLabel => IsMain ? "PRINCIPAL" : "Auxiliar";

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnP([CallerMemberName] string? n=null) => PropertyChanged?.Invoke(this, new(n));
    }

    public class SetpointFuzzyConfig : INotifyPropertyChanged
    {
        private bool _en=false;
        private int  _nPts=10;
        // Min/Max agora são automáticos — usam os valores da variável principal
        // Mantidos apenas para compatibilidade com projetos salvos em JSON
        public double Min     { get; set; } = 0;
        public double Max     { get; set; } = 100;
        public bool   Enabled { get=>_en;   set{_en=value;   OnP();} }
        public int    NPoints { get=>_nPts; set{_nPts=Math.Max(1,Math.Min(200,value)); OnP();} }
        public event PropertyChangedEventHandler? PropertyChanged;
        void OnP([CallerMemberName] string? n=null) => PropertyChanged?.Invoke(this, new(n));
    }

    public class MpcConfig : INotifyPropertyChanged
    {
        private double _pvMin=0,_pvMax=14,_sp=7,_cMin=0,_cMax=100,_cMid=50;
        private int _nPts=20; private double _alpha=3.0,_tau=1.0,_k=0.5;
        private ActionType _action=ActionType.Direta;

        // ── CORREÇÃO DO SP (linearização suave) ──────────────────────
        // Quando o SP muda sem recalcular a curva, aplica um bias
        // proporcional ao desvio SP_atual - SP_nominal da curva.
        // Ganho recomendado: 0.1 a 0.3 (suave e seguro)
        // 0.0 = sem correção | 1.0 = correção máxima (muito agressivo)
        private bool   _spCorr  = false;
        private double _spGain  = 0.15;  // ganho suave: 15% de correção por unidade
        private double _spMax   = 10.0;  // bias máximo em % do CMD range

        public double PvMin { get=>_pvMin; set{_pvMin=value; OnP(); OnP(nameof(SpNominal)); OnP(nameof(SpNominalLabel)); OnP(nameof(CmdMidEffective));} }
        public double PvMax { get=>_pvMax; set{_pvMax=value; OnP(); OnP(nameof(SpNominal)); OnP(nameof(SpNominalLabel)); OnP(nameof(CmdMidEffective));} }

        // SP — opcional. Se desabilitado usa SP_nominal = (PvMin+PvMax)/2
        private bool _spEnabled = false;
        public bool   SpEnabled { get=>_spEnabled; set{_spEnabled=value; OnP(); OnP(nameof(SpEffective)); OnP(nameof(SpNominalLabel));} }
        public double Sp        { get=>_sp;        set{_sp=value;        OnP(); OnP(nameof(SpEffective));} }

        [Newtonsoft.Json.JsonIgnore]
        public double SpEffective => _spEnabled ? _sp : SpNominal;

        [Newtonsoft.Json.JsonIgnore]
        public string SpNominalLabel => _spEnabled
            ? $"SP manual: {_sp:F3}"
            : $"SP automático = (PvMin+PvMax)/2 = {SpNominal:F3}";

        public double CmdMin { get=>_cMin; set{_cMin=value; OnP(); OnP(nameof(CmdMidEffective)); OnP(nameof(CmdMidLabel));} }
        public double CmdMax { get=>_cMax; set{_cMax=value; OnP(); OnP(nameof(CmdMidEffective)); OnP(nameof(CmdMidLabel));} }

        // CmdMid — opcional. Se desabilitado usa (CmdMin+CmdMax)/2
        private bool _cmdMidEnabled = false;
        public bool   CmdMidEnabled { get=>_cmdMidEnabled; set{_cmdMidEnabled=value; OnP(); OnP(nameof(CmdMidEffective)); OnP(nameof(CmdMidLabel));} }
        public double CmdMid        { get=>_cMid;          set{_cMid=value;          OnP(); OnP(nameof(CmdMidEffective));} }

        [Newtonsoft.Json.JsonIgnore]
        public double CmdMidEffective => _cmdMidEnabled ? _cMid : (_cMin + _cMax) / 2.0;

        [Newtonsoft.Json.JsonIgnore]
        public string CmdMidLabel => _cmdMidEnabled
            ? $"CMD_MID manual: {_cMid:F1}"
            : $"Automático = (CmdMin+CmdMax)/2 = {CmdMidEffective:F1}";
        public int    NPoints       { get=>_nPts;   set{_nPts=Math.Max(1,Math.Min(500,value)); OnP();} }
        public double Alpha         { get=>_alpha;  set{_alpha=value;  OnP();} }
        public double Tau           { get=>_tau;    set{_tau=value;    OnP();} }
        public double K             { get=>_k;      set{_k=value;      OnP();} }

        public bool   SpCorrEnabled { get=>_spCorr; set{_spCorr=value; OnP(); OnP(nameof(SpCorrLabel));} }
        public double SpGain        { get=>_spGain;
            set{_spGain=Math.Max(0,Math.Min(1.0,value)); OnP(); OnP(nameof(SpCorrLabel));} }
        public double SpBiasMax     { get=>_spMax;
            set{_spMax=Math.Max(0,Math.Min(30,value));   OnP(); OnP(nameof(SpCorrLabel));} }

        [Newtonsoft.Json.JsonIgnore]
        public string SpCorrLabel => SpCorrEnabled
            ? $"Ganho {SpGain:F2} | Bias máx ±{SpBiasMax:F1}% do CMD"
            : "Desabilitado — curva fixa";

        /// <summary>
        /// SP nominal da curva = centro do range PV configurado.
        /// Quando SP atual difere deste valor, o bias corrige suavemente.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public double SpNominal => (PvMin + PvMax) / 2.0;

        public ActionType Action { get=>_action;
            set{_action=value; OnP(); OnP(nameof(ActionLabel)); OnP(nameof(IsReverse));} }

        [Newtonsoft.Json.JsonIgnore]
        public string ActionLabel => _action switch {
            ActionType.Direta  => "Direta  — PV↑ → CMD↑  (sem inversão)",
            ActionType.Reversa => "Reversa — PV↑ → CMD↓  (com inversão)",
            _ => ""
        };

        [Newtonsoft.Json.JsonIgnore]
        public bool IsReverse => _action == ActionType.Reversa;

        // ── AMPLIFICAÇÃO MÁXIMA do CMD final ─────────────────────────
        // Ganho quando PV está nos extremos. Recomendado: 1.10 (+10%)
        private double _maxAmp = 1.10;
        public double MaxAmplification
        {
            get => _maxAmp;
            set { _maxAmp=Math.Max(1.0,Math.Min(1.30,value)); OnP(); OnP(nameof(AmpLabel)); }
        }
        [Newtonsoft.Json.JsonIgnore]
        public string AmpLabel => $"+{(_maxAmp-1.0)*100:F0}% nos extremos (fator {_maxAmp:F2}x)";

        // ── DEADBAND + HISTERESE do CMD final ────────────────────────
        // Evita micro-ajustes no atuador (válvula/motor)
        // Independente do deadband por variável (que age no fator fuzzy)
        private double _cmdDb   = 2.0;  // % do range CMD
        private double _cmdHyst = 1.0;  // % do range CMD
        private static bool _cmdInDb = false;

        public double CmdDeadband
        {
            get => _cmdDb;
            set { _cmdDb=Math.Max(0,Math.Min(20,value)); OnP(); OnP(nameof(CmdDbLabel)); }
        }
        public double CmdHysteresis
        {
            get => _cmdHyst;
            set { _cmdHyst=Math.Max(0,Math.Min(10,value)); OnP(); OnP(nameof(CmdDbLabel)); }
        }
        [Newtonsoft.Json.JsonIgnore]
        public string CmdDbLabel
        {
            get
            {
                double rng  = CmdMax - CmdMin;
                double half = rng * (_cmdDb   / 100.0) / 2.0;
                double hyst = rng * (_cmdHyst / 100.0) / 2.0;
                return $"Zona morta CMD: +/-{half:F2}  Sai em: +/-{half+hyst:F2}";
            }
        }
        public double ApplyCmdDeadband(double cmdNew, double cmdPrev)
        {
            double rng    = CmdMax - CmdMin;
            double halfDb = rng * (_cmdDb   / 100.0) / 2.0;
            double halfHy = rng * (_cmdHyst / 100.0) / 2.0;
            double dist   = Math.Abs(cmdNew - cmdPrev);
            double thresh = _cmdInDb ? (halfDb + halfHy) : halfDb;
            if (dist <= thresh) { _cmdInDb = true;  return cmdPrev; }
            _cmdInDb = false; return cmdNew;
        }

        // ── ESCALA NÃO-LINEAR (Fuzzy PID Input Scaling) ──────────────
        // Expande faixa crítica na escala do PID
        private bool   _nlEnabled  = false;
        private double _nlCritMin  = 0;
        private double _nlCritMax  = 100;
        private double _nlCritLow  = 30.0;
        private double _nlCritHigh = 70.0;

        public bool   NLEnabled  { get=>_nlEnabled;  set{_nlEnabled=value;  OnP(); OnP(nameof(NLLabel));} }
        public double NLCritMin  { get=>_nlCritMin;  set{_nlCritMin=value;  OnP(); OnP(nameof(NLLabel));} }
        public double NLCritMax  { get=>_nlCritMax;  set{_nlCritMax=value;  OnP(); OnP(nameof(NLLabel));} }
        public double NLCritLow  { get=>_nlCritLow;  set{_nlCritLow=Math.Max(0,Math.Min(49,value));   OnP();} }
        public double NLCritHigh { get=>_nlCritHigh; set{_nlCritHigh=Math.Max(51,Math.Min(100,value)); OnP();} }

        [Newtonsoft.Json.JsonIgnore]
        public string NLLabel => NLEnabled
            ? $"[{NLCritMin:F2},{NLCritMax:F2}] -> [{NLCritLow:F0}%,{NLCritHigh:F0}%]"
            : "Escala linear padrao";

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnP([CallerMemberName] string? n=null) => PropertyChanged?.Invoke(this, new(n));
    }

    public class FuzzyProject : INotifyPropertyChanged
    {
        private string _name="Novo Projeto",_desc="";
        public Guid     Id           { get;set; }=Guid.NewGuid();
        public string   Name         { get=>_name; set{_name=value; OnP();} }
        public string   Description  { get=>_desc; set{_desc=value; OnP();} }
        public DateTime SavedAt      { get;set; }=DateTime.Now;
        public ObservableCollection<FuzzyVariable> Variables     { get;set; }=new();
        public SetpointFuzzyConfig                 SetpointFuzzy { get;set; }=new();
        public MpcConfig                           Mpc           { get;set; }=new();
        public FuzzyRuleSet                        Rules         { get;set; }=new();

        // Projeto inicia SEM variáveis — usuário configura do zero
        // Use CreatePhExample() apenas para demonstração

        public static FuzzyProject CreatePhExample()
        {
            var p = new FuzzyProject { Name="Controle de pH - Exemplo",
                Description="pH (principal) + Temperatura + Nivel + Baume. Supervisor Fuzzy + MPC." };
            p.Variables.Add(new FuzzyVariable { Name="pH",Unit="pH",Min=0,Max=14,Ideal=7.0,
                Behavior=FuzzyBehavior.Otimo,NPoints=11,IsMain=true,Weight=1.0,
                TimeResponse=new(){Enabled=false} });
            p.Variables.Add(new FuzzyVariable { Name="Temperatura",Unit="C",Min=15,Max=80,Ideal=45,
                Behavior=FuzzyBehavior.Inverso,NPoints=10,Weight=0.8,
                TimeResponse=new(){Enabled=true,Value=300,Intensity=0.8} });
            p.Variables.Add(new FuzzyVariable { Name="Nivel",Unit="%",Min=0,Max=100,Ideal=50,
                Behavior=FuzzyBehavior.Direto,NPoints=10,Weight=0.6,
                TimeResponse=new(){Enabled=true,Value=30,Intensity=0.7} });
            p.Variables.Add(new FuzzyVariable { Name="Baume",Unit="Be",Min=0,Max=30,Ideal=15,
                Behavior=FuzzyBehavior.Otimo,NPoints=10,Weight=0.4,
                TimeResponse=new(){Enabled=false} });
            p.SetpointFuzzy=new(){Enabled=true,NPoints=10}; // Min/Max vem da variavel principal
            p.Mpc=new(){PvMin=0,PvMax=14,Sp=7.0,CmdMin=0,CmdMax=100,CmdMid=50,
                        NPoints=20,Alpha=3.0,Tau=1.0,K=0.5,Action=ActionType.Direta};
            return p;
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        void OnP([CallerMemberName] string? n=null) => PropertyChanged?.Invoke(this, new(n));
    }

    public enum LinearizerType { Principal, TempoResposta, Setpoint, MpcCurve, SpCorrecao, RuleTable }

    public class Linearizer
    {
        public string         Name      { get;set; }="";
        public LinearizerType Type      { get;set; }
        public List<double>   InputPts  { get;set; }=new();
        public List<double>   OutputPts { get;set; }=new();
        public string         Unit      { get;set; }="";
        public string         Notes     { get;set; }="";
        /// <summary>True se pertence a variavel principal — controla label PRINCIPAL vs AUXILIAR</summary>
        public bool           IsMainVar { get;set; } = false;
        public string TypeLabel => Type switch {
            LinearizerType.Principal     => IsMainVar ? "PRINCIPAL" : "AUXILIAR",
            LinearizerType.TempoResposta => "TEMPO_RESPOSTA",
            LinearizerType.Setpoint      => "SETPOINT",
            LinearizerType.MpcCurve      => "CURVA_MPC",
            LinearizerType.SpCorrecao    => "SP_CORRECAO",
            LinearizerType.RuleTable     => "REGRAS_FUZZY", _=>"" };
        public int    Count     => InputPts.Count;
        public double InputMin  => InputPts.Count >0 ? InputPts.Min()  : 0;
        public double InputMax  => InputPts.Count >0 ? InputPts.Max()  : 0;
        public double OutputMin => OutputPts.Count>0 ? OutputPts.Min() : 0;
        public double OutputMax => OutputPts.Count>0 ? OutputPts.Max() : 0;
        public string GeneratePlcCode()
        {
            string s=Name.Replace(" ","_").Replace("-","_");
            var sb=new System.Text.StringBuilder();
            sb.AppendLine($"// {Name} ({TypeLabel})");
            if(!string.IsNullOrEmpty(Notes)) sb.AppendLine($"// {Notes}");
            sb.AppendLine("VAR");
            sb.AppendLine($"    {s}_Input  : ARRAY[0..{Count-1}] OF REAL := ["+string.Join(", ",InputPts.Select(v=>$"{v:F3}"))+"];");
            sb.AppendLine($"    {s}_Output : ARRAY[0..{Count-1}] OF REAL := ["+string.Join(", ",OutputPts.Select(v=>$"{v:F3}"))+"];");
            sb.AppendLine("END_VAR");
            return sb.ToString();
        }
    }


    // ══════════════════════════════════════════════════════════════════
    // REGRAS FUZZY IF-THEN — Sugeno de ordem 0
    // 5 conjuntos linguísticos automáticos baseados em Min/Ideal/Max
    // ══════════════════════════════════════════════════════════════════

    public enum FuzzyTerm
    {
        MuitoBaixo,  // peak em Min
        Baixo,       // peak em (Min+Ideal)/2
        Normal,      // peak em Ideal
        Alto,        // peak em (Ideal+Max)/2
        MuitoAlto    // peak em Max
    }

    public class FuzzyCondition : INotifyPropertyChanged
    {
        private string    _varName = "";
        private FuzzyTerm _term    = FuzzyTerm.Normal;

        public string    VariableName { get=>_varName; set{_varName=value; OnP(); OnP(nameof(Label));} }
        public FuzzyTerm Term         { get=>_term;    set{_term=value;    OnP(); OnP(nameof(Label));} }

        [Newtonsoft.Json.JsonIgnore]
        public string TermLabel => Term switch {
            FuzzyTerm.MuitoBaixo => "MUITO_BAIXO",
            FuzzyTerm.Baixo      => "BAIXO",
            FuzzyTerm.Normal     => "NORMAL",
            FuzzyTerm.Alto       => "ALTO",
            FuzzyTerm.MuitoAlto  => "MUITO_ALTO",
            _ => "" };

        [Newtonsoft.Json.JsonIgnore]
        public string Label => $"{VariableName} é {TermLabel}";

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnP([CallerMemberName] string? n=null) => PropertyChanged?.Invoke(this, new(n));
    }

    public class FuzzyRule : INotifyPropertyChanged
    {
        private bool   _enabled = true;
        private double _output  = 1.0;
        private string _desc    = "";

        public bool   Enabled     { get=>_enabled; set{_enabled=value; OnP(); OnP(nameof(StatusIcon));} }
        public double Output      { get=>_output;  set{_output=Math.Max(0,Math.Min(1.5,value)); OnP(); OnP(nameof(OutputLabel));} }
        public string Description { get=>_desc;    set{_desc=value;    OnP();} }

        public ObservableCollection<FuzzyCondition> Conditions { get; set; } = new();

        [Newtonsoft.Json.JsonIgnore]
        public string StatusIcon  => _enabled ? "✅" : "⬜";

        [Newtonsoft.Json.JsonIgnore]
        public string OutputLabel => _output switch {
            double v when v >= 1.15 => $"{v:F2} ↑↑ amplifica muito",
            double v when v >= 1.05 => $"{v:F2} ↑  amplifica",
            double v when v >= 0.95 => $"{v:F2} =  neutro",
            double v when v >= 0.50 => $"{v:F2} ↓  atenua",
            _                       => $"{_output:F2} ↓↓ atenua muito" };

        [Newtonsoft.Json.JsonIgnore]
        public string RuleText
        {
            get
            {
                if (!Conditions.Any()) return "(sem condições)";
                string conds = string.Join(" E ", Conditions.Select(c => c.Label));
                return $"SE {conds} ENTÃO fator = {_output:F2}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnP([CallerMemberName] string? n=null) => PropertyChanged?.Invoke(this, new(n));
    }

    public class FuzzyRuleSet : INotifyPropertyChanged
    {
        private bool   _enabled  = false;
        private int    _nPoints  = 20;
        private string _outUnit  = "fator";

        public bool   Enabled  { get=>_enabled;  set{_enabled=value;  OnP();} }
        public int    NPoints  { get=>_nPoints;  set{_nPoints=Math.Max(4,Math.Min(200,value)); OnP();} }
        public string OutUnit  { get=>_outUnit;  set{_outUnit=value;  OnP();} }

        public ObservableCollection<FuzzyRule> Rules { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnP([CallerMemberName] string? n=null) => PropertyChanged?.Invoke(this, new(n));
    }

    public class LinearizerRow { public int Pt{get;set;} public double Input{get;set;} public double Output{get;set;} }
    public class AuxVarState   { public string Name{get;set;}=""; public double Value{get;set;} public double Mu{get;set;} public double Weight{get;set;} public double WxMu{get;set;} }
    public class MpcPoint      { public int Index{get;set;} public double Pv{get;set;} public double Z{get;set;} public double Cmd{get;set;} public string Zone{get;set;}=""; }
    public class CalcResult
    {
        public double              Severity   { get;set; }
        public double              TauEf      { get;set; }
        public double              AlphaEf    { get;set; }
        public double              A          { get;set; }
        public List<MpcPoint>      MpcCurve   { get;set; }=new();
        public List<AuxVarState>   AuxStates  { get;set; }=new();
        public List<Linearizer>    Linearizers{ get;set; }=new();
    }
}
