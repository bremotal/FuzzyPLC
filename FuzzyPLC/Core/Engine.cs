using System.Collections.Generic;
using System.Linq;
using FuzzyPLC.Models;
using static FuzzyPLC.Models.ActionType;

namespace FuzzyPLC.Core
{
    public static class FuzzyCalculator
    {
        /// <summary>
        /// Calcula fator de correcao fuzzy — saida sempre em [0.0, 1.0]
        /// 0.0 = valvula fechada / motor parado
        /// 1.0 = valvula 100% aberta / motor velocidade maxima
        /// </summary>
        // Estado estático para histerese — rastreia se cada variável está na zona morta
        // Key = variável nome, Value = true se está dentro da deadband
        private static readonly Dictionary<string,bool> _inDeadband = new();

        public static double CorrectionFactor(double valor, FuzzyVariable v)
        {
            double range = v.Max - v.Min;
            if (range <= 0) return 1.0;

            double dif   = valor - v.Ideal;
            double dist  = Math.Abs(dif);

            // ── ZONA MORTA + HISTERESE ────────────────────────────────
            // Deadband: faixa onde fator = 1.0 (sem correção)
            // Histerese: aumenta a faixa de saída evitando chatter
            //   Entra na zona morta: dist < deadband/2
            //   Sai da zona morta:   dist > (deadband/2 + hysteresis/2)
            double halfDb = range * (v.Deadband    / 100.0) / 2.0;
            double halfHy = range * (v.Hysteresis  / 100.0) / 2.0;

            bool wasInDb = _inDeadband.TryGetValue(v.Name, out bool prev) && prev;
            double threshold = wasInDb ? (halfDb + halfHy) : halfDb;

            if (dist <= threshold)
            {
                _inDeadband[v.Name] = true;
                return 1.0;  // estável — sem correção
            }
            _inDeadband[v.Name] = false;

            // Distância efetiva (desconta a deadband)
            double distEf = dist - halfDb;
            double raw    = 1.0;
            // Amplificação por variável: fixa em 1.10 (+10%)
            // A amplificação configurável está no MpcConfig (age no CMD final)
            const double amp = 1.10;

            switch (v.Behavior)
            {
                case FuzzyBehavior.Otimo:
                    if (dif < 0) { double d=(v.Ideal-halfDb)-v.Min; raw = d>0 ? 1.0+(distEf/d)*(amp-1.0) : 1.0; }
                    else         { double d=v.Max-(v.Ideal+halfDb);  raw = d>0 ? 1.0-(distEf/d)*0.5       : 1.0; }
                    break;
                case FuzzyBehavior.Direto:
                    if (dif < 0) { double d=(v.Ideal-halfDb)-v.Min; raw = d>0 ? 1.0+(distEf/d)*(amp-1.0) : 1.0; }
                    else         { double d=v.Max-(v.Ideal+halfDb);  raw = d>0 ? 1.0-(distEf/d)*0.3       : 1.0; }
                    break;
                case FuzzyBehavior.Inverso:
                    if (dif > 0) { double d=v.Max-(v.Ideal+halfDb);  raw = d>0 ? 1.0+(distEf/d)*(amp-1.0) : 1.0; }
                    else         { double d=(v.Ideal-halfDb)-v.Min;  raw = d>0 ? 1.0-(distEf/d)*0.3       : 1.0; }
                    break;
                default: raw = 1.0; break;
            }

            return Math.Max(0.0, Math.Min(amp, raw));
        }

        // ── ESCALA NÃO-LINEAR (Fuzzy PID Input Scaling) ──────────────
        // Converte o valor bruto do sensor para uma escala expandida
        // dando mais resolução/sensibilidade na faixa crítica.
        //
        // Exemplo álcool:
        //   Sensor:   [92.0 ────── 92.7 ─── 93.1 ────── 94.0]
        //   Saída:    [0%   ──────  30% ───  70% ──────  100%]
        //   Resultado: faixa crítica [92.7,93.1] ocupa 40% da escala
        //              em vez de apenas 20% → PID 2x mais sensível
        //
        // ── NonLinearScale para FuzzyVariable (deadband/histerese por variável)
        public static double NonLinearScale(double valor, FuzzyVariable v)
        {
            return valor; // NL agora é configurado no MpcConfig, não por variável
        }

        // ── NonLinearScale para MpcConfig (escala não-linear no CMD final)
        public static double NonLinearScaleMpc(double valor, MpcConfig m)
        {
            if (!m.NLEnabled) return valor;

            double pvRng     = m.PvMax - m.PvMin;
            double critRange = m.NLCritMax - m.NLCritMin;
            if (pvRng <= 0 || critRange <= 0) return valor;

            double outRange = m.NLCritHigh - m.NLCritLow;
            double result;

            if (valor <= m.NLCritMin)
            {
                double seg = m.NLCritMin - m.PvMin;
                if (seg <= 0) return m.PvMin + (m.NLCritLow/100.0)*pvRng;
                double t = (valor - m.PvMin) / seg;
                result = m.NLCritLow * t;
            }
            else if (valor <= m.NLCritMax)
            {
                double t = (valor - m.NLCritMin) / critRange;
                result = m.NLCritLow + t * outRange;
            }
            else
            {
                double seg = m.PvMax - m.NLCritMax;
                if (seg <= 0) return m.PvMin + (m.NLCritHigh/100.0)*pvRng;
                double t = (valor - m.NLCritMax) / seg;
                result = m.NLCritHigh + t * (100.0 - m.NLCritHigh);
            }

            return m.PvMin + (result / 100.0) * pvRng;
        }

        public static (List<double> entrada, List<double> saida)
            MembershipCurve(FuzzyVariable v)
        {
            var ent = Linspace(v.Min, v.Max, v.NPoints);
            var saida = ent.Select(x => {
                // Aplica re-escala não-linear se habilitada (Fuzzy PID Input Scaling)
                // Isso expande a faixa crítica na curva, dando mais resolução ao PID
                double xNL = FuzzyCalculator.NonLinearScale(x, v);
                return Math.Round(CorrectionFactor(xNL, v), 4);
            }).ToList();
            return (ent, saida);
        }

        public static (List<double> entrada, List<double> saida)
            TimeResponseCurve(double tau, double intensity)
        {
            List<double> ent, raw;
            if      (tau <= 60 ) { ent=new(){10,30,60,120,300,600};   raw=new(){1.3,1.2,1.1,1.0,0.9,0.8}; }
            else if (tau <= 180) { ent=new(){30,60,120,240,480,600};  raw=new(){1.2,1.1,1.0,0.8,0.6,0.5}; }
            else                 { ent=new(){60,120,240,480,600,900}; raw=new(){1.1,1.0,0.7,0.5,0.4,0.3}; }

            // Aplica intensidade e garante [0.0, 1.0] — padrao industrial
            var saida = raw.Select(s => {
                double val = 1.0 + (s - 1.0) * intensity;
                return Math.Round(Math.Max(0.0, Math.Min(1.0, val)), 3);
            }).ToList();

            return (ent.Select(e => Math.Round(e,1)).ToList(), saida);
        }

        public static (List<double> entrada, List<double> saida)
            SetpointCurve(double min, double max, int nPts)
        {
            var ent    = Linspace(min, max, nPts);
            double ctr = (min+max)/2.0, mrg = (max-min)*0.2;
            var saida  = ent.Select(sp => {
                if (Math.Abs(sp-ctr) < mrg) return 1.0;
                double dl = Math.Min(Math.Abs(sp-min), Math.Abs(sp-max));
                return Math.Round(Math.Max(0.0, Math.Min(1.0, mrg>0 ? 0.7+(dl/mrg)*0.3 : 1.0)), 3);
            }).ToList();
            return (ent.Select(v => Math.Round(v,3)).ToList(), saida);
        }

        public static List<double> Linspace(double a, double b, int n)
        {
            if (n<=1) return new(){a};
            double step=(b-a)/(n-1);
            return Enumerable.Range(0,n).Select(i => Math.Round(a+i*step,10)).ToList();
        }
    }

    public static class MpcEngine
    {
        public static (double S, List<AuxVarState> states)
            Severity(IList<FuzzyVariable> vars, IList<double> currentValues)
        {
            var st=new List<AuxVarState>(); double sw=0,smw=0;
            for (int i=0;i<vars.Count;i++) {
                var v=vars[i];
                double val=i<currentValues.Count?currentValues[i]:v.Ideal;
                double rng=v.Max-v.Min;
                double mu=rng>0?Math.Max(0,Math.Min(1,Math.Abs(val-v.Ideal)/rng)):0;
                double wxmu=mu*v.Weight; smw+=wxmu; sw+=v.Weight;
                st.Add(new AuxVarState{Name=v.Name,Value=val,Mu=Math.Round(mu,4),
                    Weight=v.Weight,WxMu=Math.Round(wxmu,4)});
            }
            return (Math.Round(sw>0?smw/sw:0,4), st);
        }

        public static (double tauEf, double alphaEf) Adjust(double tau, double alpha, double k, double S)
        {
            double tef=tau*(1.0+S), aef=alpha/(1.0+k*tef);
            return (Math.Round(tef,4), Math.Round(aef,4));
        }

        public static List<MpcPoint> Curve(MpcConfig p, double alphaEf,
                                            double mainVarDeadband = 5.0)
        {
            double pvRng  = p.PvMax - p.PvMin;
            double A      = (p.CmdMax - p.CmdMin) / 2.0;
            double cmdRng = p.CmdMax - p.CmdMin;

            // ── Zona IDEAL: usa deadband da variável principal ────────
            // Converte % da faixa PV para unidade real
            // Ex: deadband=5%, pvRng=0.4°GL → pvHalf = 0.4×0.05/2 = ±0.01°GL
            // Garante mínimo de 1% do pvRng para ser visível
            double pvHalf = Math.Max(
                pvRng * (mainVarDeadband / 100.0) / 2.0,
                pvRng * 0.01);

            // ── Correção do SP (bias suave) ───────────────────────────
            double spBias = 0;
            if (p.SpCorrEnabled && pvRng > 0)
            {
                double spNominal = (p.PvMin + p.PvMax) / 2.0;
                double spDesvio  = p.SpEffective - spNominal;
                double biasRaw   = spDesvio * p.SpGain * cmdRng / pvRng;
                double biasLim   = cmdRng * (p.SpBiasMax / 100.0);
                spBias = Math.Max(-biasLim, Math.Min(biasLim, biasRaw));
                if (p.Action == ActionType.Reversa) spBias = -spBias;
            }

            var pts = new List<MpcPoint>();
            for (int i = 0; i < p.NPoints; i++)
            {
                double pv = p.NPoints > 1
                    ? p.PvMin + (double)i / (p.NPoints - 1) * pvRng
                    : p.PvMin;

                double pvNL  = FuzzyCalculator.NonLinearScaleMpc(pv, p);
                double z     = pvRng > 0 ? (pvNL - p.SpEffective) / pvRng : 0;
                double az    = Math.Max(-0.9999, Math.Min(0.9999, alphaEf * z));
                double atanh = 0.5 * Math.Log((1 + az) / (1 - az));
                double cmd   = p.CmdMidEffective + A * atanh;

                // Tipo de ação
                if (p.Action == ActionType.Reversa)
                    cmd = p.CmdMax - (cmd - p.CmdMin);

                // Aplica bias do SP
                cmd += spBias;

                // Amplificação máxima
                double cmdCenter = (p.CmdMin + p.CmdMax) / 2.0;
                double maxDist   = (p.CmdMax - p.CmdMin) / 2.0 * p.MaxAmplification;
                double distCmd   = cmd - cmdCenter;
                if (Math.Abs(distCmd) > maxDist)
                    cmd = cmdCenter + Math.Sign(distCmd) * maxDist;

                // Garantia industrial
                cmd = Math.Max(p.CmdMin, Math.Min(p.CmdMax, cmd));

                // IDEAL = PV dentro do deadband da variável principal
                // Nesta faixa o atuador mantém posição → estabilidade
                string zone = Math.Abs(pv - p.SpEffective) <= pvHalf ? "IDEAL"
                            : pv < p.SpEffective ? "ABAIXO" : "ACIMA";

                pts.Add(new MpcPoint {
                    Index = i + 1,
                    Pv    = Math.Round(pv,  4),
                    Z     = Math.Round(z,   4),
                    Cmd   = Math.Round(cmd, 3),
                    Zone  = zone
                });
            }
            return pts;
        }

        // ══════════════════════════════════════════════════════════════════
        // CALCULO COMPLETO — Arquitetura separada por variavel
        //
        // FLUXO:
        //   1. Para CADA variavel → gera linearizador individual (0.0 a 1.0)
        //      Entrada: valor real da variavel (ex: pH 0-14)
        //      Saida:   fator de acao [0.0=minimo, 1.0=maximo]
        //
        //   2. Variaveis auxiliares → calculam severidade S (supervisor fuzzy)
        //      S ajusta os parametros tau e alpha do MPC
        //
        //   3. Variavel PRINCIPAL → gera CURVA_MPC com ajuste fuzzy
        //      Entrada: PV da variavel principal
        //      Saida:   CMD [CmdMin, CmdMax] para o atuador
        //
        //   4. No PLC: operador combina os linearizadores (AND/logica propria)
        //      Cada linearizador e independente e exportado separadamente
        // ══════════════════════════════════════════════════════════════════
        public static CalcResult Calculate(FuzzyProject proj, List<double>? currentValues=null)
        {
            var lins = new List<Linearizer>();

            // ── PASSO 1: Linearizador individual para CADA variavel ───────
            // Cada variavel gera sua propria curva independente
            // O PLC vai usar cada tabela separadamente e combinar conforme logica do processo
            var mainVar = proj.Variables.FirstOrDefault(v => v.IsMain);
            int varIdx  = 0;

            foreach (var v in proj.Variables)
            {
                string role = v.IsMain ? "PRINCIPAL" : "AUXILIAR";
                var (ent, saida) = FuzzyCalculator.MembershipCurve(v);

                lins.Add(new Linearizer {
                    Name      = v.IsMain ? $"Lin_{v.Name}_PRINCIPAL" : $"Lin_{v.Name}",
                    Type      = LinearizerType.Principal,
                    IsMainVar = v.IsMain,
                    InputPts  = ent,
                    OutputPts = saida,
                    Unit      = v.Unit,
                    Notes     = $"[{role}] {v.BehaviorLabel} | " +
                                $"Range:[{v.Min},{v.Max}] Ideal={v.Ideal} | " +
                                $"Saida:[0.0=minimo, 1.0=maximo] | " +
                                $"Peso p={v.Weight} | " +
                                $"Pontos:{v.NPoints}"
                });

                // ── Tempo de resposta — incorporado no próprio linearizador ──
                // Em vez de criar tabela separada, aplica o fator de tempo
                // diretamente sobre os valores de saída do linearizador principal.
                // Lógica: saida_final = saida_fuzzy * fator_tempo(tau, intensidade)
                // Onde fator_tempo(t) = 1.0 + (curva_tempo - 1.0) * intensidade
                // Para t próximo de 0 (resposta rápida): fator > 1.0 (amplifica)
                // Para t grande (resposta lenta):        fator < 1.0 (atenua)
                if (v.TimeResponse.Enabled)
                {
                    // Calcula fator baseado no tau configurado
                    // Usa o ponto médio da curva de tempo como referência
                    double tau       = v.TimeResponse.Value;
                    double intensity = v.TimeResponse.Intensity;

                    // Fator de tempo: quanto o tau atual influencia a saída
                    // tau pequeno (rápido) → fator > 1 → amplifica ação imediata
                    // tau grande (lento)   → fator < 1 → atenua ação (espera estabilizar)
                    double tFator;
                    if      (tau <= 60)  tFator = 1.1;   // rápido: amplifica 10%
                    else if (tau <= 180) tFator = 1.0;   // médio: neutro
                    else                tFator = 0.8;    // lento: atenua 20%

                    // Aplica intensidade: fator_final = 1.0 + (tFator-1.0) * intensity
                    double fatorFinal = 1.0 + (tFator - 1.0) * intensity;
                    fatorFinal = Math.Max(0.0, Math.Min(1.0, fatorFinal));

                    // Modifica saída do linearizador já criado (último da lista)
                    var linAtual = lins[lins.Count - 1];
                    linAtual.OutputPts = linAtual.OutputPts
                        .Select(s => Math.Max(0.0, Math.Min(1.0, s * fatorFinal)))
                        .ToList();
                    linAtual.Notes += $" | Tempo aplicado: tau={tau}s " +
                                      $"cat={v.TimeResponse.Category} " +
                                      $"fator={fatorFinal:F3}";
                }
                varIdx++;
            }

            // Setpoint fuzzy (se habilitado)
            // MODULAR: usa Min/Max da variável principal automaticamente
            // O usuário NÃO precisa configurar Min/Max do setpoint separadamente
            if (proj.SetpointFuzzy.Enabled) {
                var sp = proj.SetpointFuzzy;

                // Pega Min/Max da variável principal (se existir)
                // Isso torna o setpoint 100% modular — muda a variável, muda o setpoint
                double spMin = mainVar?.Min ?? sp.Min;
                double spMax = mainVar?.Max ?? sp.Max;
                int    spPts = sp.NPoints > 0 ? sp.NPoints : 10;

                var (es, os) = FuzzyCalculator.SetpointCurve(spMin, spMax, spPts);
                lins.Add(new Linearizer {
                    Name     = "Lin_Setpoint",
                    Type     = LinearizerType.Setpoint,
                    InputPts = es, OutputPts = os,
                    Notes    = $"Curva fuzzy do setpoint | " +
                               $"Range:[{spMin},{spMax}] " +
                               $"(baseado na variavel principal: {mainVar?.Name ?? "nao definida"}) | " +
                               $"Pontos:{spPts}"
                });
            }

            // ── PASSO 2: Supervisor fuzzy — calcula severidade S ─────────
            // Usa apenas as variaveis AUXILIARES para calcular S
            // S representa o quanto as condicoes estao afastadas do ideal
            var aux  = proj.Variables.Where(v => !v.IsMain).ToList();
            var vals = currentValues ?? aux.Select(v => v.Ideal).ToList();
            var (S, states) = Severity(aux, vals);

            // ── PASSO 3: Ajuste dinamico e curva MPC ─────────────────────
            // S ajusta tau e alpha, que modificam a curva MPC da variavel principal
            var (tauEf, alphaEf) = Adjust(proj.Mpc.Tau, proj.Mpc.Alpha, proj.Mpc.K, S);
            // Passa deadband da variável principal para zona IDEAL da curva
            double mainDbPct = mainVar?.Deadband ?? 5.0;
            var mpcCurve = Curve(proj.Mpc, alphaEf, mainDbPct);

            // Adiciona curva MPC como linearizador da variavel principal
            string mainName = mainVar?.Name ?? "PrincipalVar";
            lins.Add(new Linearizer {
                Name      = $"CurvaMPC_{mainName}",
                Type      = LinearizerType.MpcCurve,
                InputPts  = mpcCurve.Select(pt => pt.Pv).ToList(),
                OutputPts = mpcCurve.Select(pt => pt.Cmd).ToList(),
                Notes     = $"Curva MPC ajustada pelo supervisor fuzzy | " +
                            $"S={S:F4} | tauEf={tauEf:F4} | alphaEf={alphaEf:F4} | " +
                            $"CMD:[{proj.Mpc.CmdMin}=fechado, {proj.Mpc.CmdMax}=aberto] | " +
                            $"NOTA: combine com os outros linearizadores no PLC conforme logica do processo"
            });

            // ── PASSO 4: Linearizador de Correção do SP ───────────────
            // Entrada: valor do SP atual (range PvMin..PvMax)
            // Saída:   FATOR MULTIPLICADOR normalizado [0.0 .. 2.0]
            //   1.0 = sem correção (SP está no nominal)
            //   >1.0 = amplifica CMD (SP acima do nominal + ação Direta)
            //   <1.0 = atenua CMD (SP abaixo do nominal + ação Direta)
            //
            // Uso no PLC:
            //   fator = LINEARIZE(Lin_SP_Correcao, SP_atual)
            //   CMD_final = LINEARIZE(CurvaMPC, PV) * fator
            //   → Quando SP=nominal → fator=1.0 → sem alteração
            //   → Quando SP muda   → fator≠1.0 → ajuste suave
            if (proj.Mpc.SpCorrEnabled)
            {
                var m        = proj.Mpc;
                double pvRng = m.PvMax - m.PvMin;
                double cmdRng= m.CmdMax - m.CmdMin;
                double spNom = (m.PvMin + m.PvMax) / 2.0;
                double cmdMid= (m.CmdMin + m.CmdMax) / 2.0;
                double biasLim = cmdRng * (m.SpBiasMax / 100.0);

                int nPtsSp = Math.Max(10, proj.Mpc.NPoints);
                var spInputs = FuzzyCalculator.Linspace(m.PvMin, m.PvMax, nPtsSp);

                var spFactors = spInputs.Select(sp => {
                    // Calcula bias em unidades CMD
                    double desvio  = sp - spNom;
                    double biasRaw = pvRng > 0 ? desvio * m.SpGain * cmdRng / pvRng : 0;
                    double bias    = Math.Max(-biasLim, Math.Min(biasLim, biasRaw));
                    if (m.Action == ActionType.Reversa) bias = -bias;

                    // Converte bias para fator multiplicador
                    // fator = (cmdMid + bias) / cmdMid
                    // Quando bias=0 → fator=1.0 (sem alteração)
                    // Quando bias>0 → fator>1.0 (amplifica)
                    // Quando bias<0 → fator<1.0 (atenua)
                    double fator = cmdMid > 0
                        ? (cmdMid + bias) / cmdMid
                        : 1.0;

                    // Limita: [0.5 .. 1.5] — seguro para industrial
                    fator = Math.Max(0.5, Math.Min(1.5, fator));
                    return Math.Round(fator, 4);
                }).ToList();

                lins.Add(new Linearizer {
                    Name      = "Lin_SP_Correcao",
                    Type      = LinearizerType.SpCorrecao,
                    InputPts  = spInputs.Select(v => Math.Round(v, 4)).ToList(),
                    OutputPts = spFactors,
                    Notes     = $"FATOR MULTIPLICADOR do CMD | " +
                                $"SP nominal={spNom:F3} | " +
                                $"Ganho={m.SpGain:F2} | Bias max=±{m.SpBiasMax:F1}% | " +
                                $"Acao={m.Action} | " +
                                $"1.0=sem correcao | >1.0=amplifica | <1.0=atenua | " +
                                $"Uso PLC: fator=LINEARIZE(Lin_SP_Correcao,SP) | CMD_final=CurvaMPC(PV)*fator"
                });
            }

            return new CalcResult {
                Severity    = S,
                TauEf       = tauEf,
                AlphaEf     = alphaEf,
                A           = Math.Round((proj.Mpc.CmdMax - proj.Mpc.CmdMin) / 2.0, 3),
                MpcCurve    = mpcCurve,
                AuxStates   = states,
                Linearizers = lins
            };
        }
    }
}
