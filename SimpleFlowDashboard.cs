using cAlgo.API;
using cAlgo.API.Indicators;
using System;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class SimpleFlowDashboard_v20 : Robot
    {
        [Parameter("EMA8 Proximity Threshold", DefaultValue = 0.45)]
        public double Ema8ProximityThreshold { get; set; }

        [Parameter("Pivot Strength", DefaultValue = 3)]
        public int PivotStrength { get; set; }

        [Parameter("Min Swing Size", DefaultValue = 100)]
        public double MinSwingSize { get; set; }

        [Parameter("Max Swing Life Bars", DefaultValue = 36)]
        public int MaxSwingLifeBars { get; set; }

        [Parameter("Max Candidate Life Bars", DefaultValue = 6)]
        public int MaxCandidateLifeBars { get; set; }

        private Bars _h1Bars;
        private Bars _d1Bars;

        private ExponentialMovingAverage _d1Ema8;
        private ExponentialMovingAverage _d1Ema21;
        private ExponentialMovingAverage _d1Ema50;

        private ExponentialMovingAverage _h1Ema8;
        private ExponentialMovingAverage _h1Ema21;
        private ExponentialMovingAverage _h1Ema50;

        private AverageTrueRange _d1Atr20;
        private AverageTrueRange _h1Atr20;

        private enum EstadoSwing
        {
            BuscandoSwing,
            SwingCandidato,
            SwingActivo,
            SwingCongelado
        }

        private EstadoSwing _estado = EstadoSwing.BuscandoSwing;

        private string _direccion = "NO";

        private int _originBar = -1;
        private int _endBar = -1;
        private int _activatedBar = -1;
        // --- MEMORIA ESTRUCTURAL CONGELADA D1 ---
        private double _lastConfirmedD1SwingHigh = 0.0;
        private double _lastConfirmedD1SwingLow = 0.0;

        private double _originPrice = 0;
        private double _endPrice = 0;

        private double _level0 = 0;
        private double _level382 = 0;
        private double _level50 = 0;
        private double _level618 = 0;
        private double _level764 = 0;
        private double _level100 = 0;
        private double _levelMinus21 = 0;

        protected override void OnStart()
        {
            _h1Bars = MarketData.GetBars(TimeFrame.Hour);
            _d1Bars = MarketData.GetBars(TimeFrame.Daily);

            _d1Ema8 = Indicators.ExponentialMovingAverage(_d1Bars.ClosePrices, 8);
            _d1Ema21 = Indicators.ExponentialMovingAverage(_d1Bars.ClosePrices, 21);
            _d1Ema50 = Indicators.ExponentialMovingAverage(_d1Bars.ClosePrices, 50);

            _h1Ema8 = Indicators.ExponentialMovingAverage(_h1Bars.ClosePrices, 8);
            _h1Ema21 = Indicators.ExponentialMovingAverage(_h1Bars.ClosePrices, 21);
            _h1Ema50 = Indicators.ExponentialMovingAverage(_h1Bars.ClosePrices, 50);

            _d1Atr20 = Indicators.AverageTrueRange(_d1Bars, 20, MovingAverageType.Exponential);
            _h1Atr20 = Indicators.AverageTrueRange(_h1Bars, 20, MovingAverageType.Exponential);

            Print("Simple Flow Dashboard v2.0 iniciado correctamente.");
            DrawDashboard();
        }

        protected override void OnTick()
        {
            if (_estado == EstadoSwing.SwingCongelado)
            {
                int index = _h1Bars.Count - 1;

                if (EvaluarResolucion(index))
                {
                    ReiniciarSwing();
                    Print("SWING BORRADO EN TIEMPO REAL: tocó 100% o -21%.");
                }
            }

            GestionarDibujo();
            DrawDashboard();
        }

        protected override void OnBar()
        {
            int index = _h1Bars.Count - 2;

            if (index < PivotStrength * 2 + 120)
            {
                DrawDashboard();
                return;
            }

            ProcesarSwingFibo(index);
            GestionarDibujo();
            DrawDashboard();
        }

        private void DrawDashboard()
        {
            int d1Index = _d1Bars.Count - 2;
            int h1Index = _h1Bars.Count - 2;

            if (d1Index < 120 || h1Index < 120)
            {
                DrawTextPanel("Esperando datos...");
                return;
            }

            double d1Close = _d1Bars.ClosePrices[d1Index];
            double d1Ema8 = _d1Ema8.Result[d1Index];
            double d1Atr = _d1Atr20.Result[d1Index];
            double h1Atr = _h1Atr20.Result[h1Index];

            double atrPercent = (d1Atr > 0) ? (h1Atr / d1Atr) * 100 : 0;

            string atrStatus = "";

             if (atrPercent <= 15.0)
             {
                atrStatus = "Sin volumen";
             }
             else if (atrPercent > 15.0 && atrPercent <= 30.0)
             {
                atrStatus = "Riesgo de operativa";
             }
             else
             {
                atrStatus = "Volatilidad optima"; // Rango mayor al 30%
             }

                       double distEma8 = Math.Abs(d1Close - d1Ema8);
                       double maxDist = d1Atr * Ema8ProximityThreshold;

            string d1Trend = GetD1Trend(d1Index);
            string d1NearEma8 = distEma8 < maxDist ? "En Rango | Check (.45)" : "Extendido | Pend (.45)";

            string d1EmasAligned = GetD1EmasAlineacionStatus(d1Index);
            string d1Phase = GetD1Phase(d1Trend, d1NearEma8, d1Index);
            string d1Health = GetD1Health(d1Index, d1Trend);
            string d1Momentum = GetD1Momentum(d1Index);

            string h1Trend = GetH1Trend(h1Index);
            string sync = GetSync(d1Trend, h1Trend);
            string h1Structure = GetH1Structure(h1Index, h1Trend);
            string h1Health = GetH1Health(h1Index, h1Trend);
            string h1Momentum = GetH1Momentum(h1Index);
            string swingStatus = GetSwingStatusText();

        
            string dashboard =
            $@"D1 ATR20 : {d1Atr:F1}
H1 ATR20 : {h1Atr:F1} ({atrPercent:F0}%) - {atrStatus}

--- D1 ---
Trend      : {d1Trend}
EMAs      : {d1EmasAligned}
Carril 8    : {d1NearEma8}
Phase      : {d1Phase}
Fuerza MKD : {d1Health}
Mom        : {d1Momentum}

--- H1 ---
Trend      : {h1Trend}
Sync       : {sync}
Struct     : {h1Structure}
Fuerza MKD : {h1Health}
Mom        : {h1Momentum}
Swing      : {swingStatus}";

            DrawTextPanel(dashboard);
        }

        private void ProcesarSwingFibo(int index)
        {
            int candidate = index - PivotStrength;

            bool emaBullish = _h1Ema8.Result[index] > _h1Ema21.Result[index] && _h1Ema21.Result[index] > _h1Ema50.Result[index];
            bool emaBearish = _h1Ema8.Result[index] < _h1Ema21.Result[index] && _h1Ema21.Result[index] < _h1Ema50.Result[index];

            bool pivotLow = IsH1SwingLow(candidate, PivotStrength);
            bool pivotHigh = IsH1SwingHigh(candidate, PivotStrength);

            if (_estado == EstadoSwing.BuscandoSwing)
            {
                if (emaBearish && pivotHigh)
                    CrearCandidatoBajista(candidate);
                else if (emaBullish && pivotLow)
                    CrearCandidatoAlcista(candidate);
            }

            if (_estado == EstadoSwing.SwingCandidato)
            {
                ActualizarFinDinamico(index);
                CalcularNiveles();

                int candidateAge = index - _originBar;
                double range = Math.Abs(_originPrice - _endPrice);
                bool swingTieneMasDeUnaVela = _originBar != _endBar;

                if (candidateAge >= MaxCandidateLifeBars)
                {
                    Print("SWING CANDIDATO EXPIRADO: no alcanzó rango mínimo dentro del tiempo permitido.");
                    ReiniciarSwing();
                    return;
                }

                if (range >= MinSwingSize && swingTieneMasDeUnaVela)
                {
                    _estado = EstadoSwing.SwingActivo;
                    _activatedBar = index;
                    Print("SWING ACTIVO: rango mínimo validado y más de una vela.");
                }
            }

            if (_estado == EstadoSwing.SwingActivo)
            {
                int activeAge = index - _activatedBar;

                if (activeAge >= MaxSwingLifeBars)
                {
                    Print("SWING ACTIVO EXPIRADO: superó vida máxima de {0} barras.", MaxSwingLifeBars);
                    ReiniciarSwing();
                    return;
                }

                ActualizarFinDinamico(index);
                CalcularNiveles();

                if (TocoZonaPullback50(index))
                {
                    _estado = EstadoSwing.SwingCongelado;
                    Print("SWING CONGELADO: precio tocó 50%.");
                }
            }

            if (_estado == EstadoSwing.SwingCongelado)
            {
                if (EvaluarResolucion(index))
                {
                    ReiniciarSwing();
                    Print("SWING BORRADO: resolución alcanzada. Buscando nuevo swing.");
                    return;
                }
            }
        }

        private void CrearCandidatoBajista(int bar)
        {
            _direccion = "BAJISTA";

            _originBar = bar;
            _originPrice = _h1Bars.HighPrices[bar];

            _endBar = bar;
            _endPrice = _h1Bars.LowPrices[bar];

            _activatedBar = -1;

            CalcularNiveles();
            _estado = EstadoSwing.SwingCandidato;

            Print("SWING CANDIDATO BAJISTA CREADO.");
        }

        private void CrearCandidatoAlcista(int bar)
        {
            _direccion = "ALCISTA";

            _originBar = bar;
            _originPrice = _h1Bars.LowPrices[bar];

            _endBar = bar;
            _endPrice = _h1Bars.HighPrices[bar];

            _activatedBar = -1;

            CalcularNiveles();
            _estado = EstadoSwing.SwingCandidato;

            Print("SWING CANDIDATO ALCISTA CREADO.");
        }

        private void ActualizarFinDinamico(int index)
        {
            if (_direccion == "BAJISTA")
            {
                if (index != _originBar && _h1Bars.LowPrices[index] < _endPrice)
                {
                    _endPrice = _h1Bars.LowPrices[index];
                    _endBar = index;
                }
            }

            if (_direccion == "ALCISTA")
            {
                if (index != _originBar && _h1Bars.HighPrices[index] > _endPrice)
                {
                    _endPrice = _h1Bars.HighPrices[index];
                    _endBar = index;
                }
            }
        }

        private void CalcularNiveles()
        {
            double range = Math.Abs(_originPrice - _endPrice);

            if (_direccion == "BAJISTA")
            {
                _level100 = _originPrice;
                _level0 = _endPrice;
                _level764 = _endPrice + range * 0.764;
                _level618 = _endPrice + range * 0.618;
                _level50 = _endPrice + range * 0.50;
                _level382 = _endPrice + range * 0.382;
                _levelMinus21 = _endPrice - range * 0.21;
            }

            if (_direccion == "ALCISTA")
            {
                _level100 = _originPrice;
                _level0 = _endPrice;
                _level764 = _endPrice - range * 0.764;
                _level618 = _endPrice - range * 0.618;
                _level50 = _endPrice - range * 0.50;
                _level382 = _endPrice - range * 0.382;
                _levelMinus21 = _endPrice + range * 0.21;
            }
        }

        private bool TocoZonaPullback50(int index)
        {
            if (_direccion == "BAJISTA")
                return _h1Bars.HighPrices[index] >= _level50;

            if (_direccion == "ALCISTA")
                return _h1Bars.LowPrices[index] <= _level50;

            return false;
        }

        private bool EvaluarResolucion(int index)
        {
            if (_direccion == "BAJISTA")
            {
                bool toco100 = _h1Bars.HighPrices[index] >= _level100;
                bool tocoMinus21 = _h1Bars.LowPrices[index] <= _levelMinus21;

                if (toco100)
                {
                    Print("RESOLUCION: INVALIDACION BAJISTA | TOCO 100%");
                    return true;
                }

                if (tocoMinus21)
                {
                    Print("RESOLUCION: CONTINUACION BAJISTA | TOCO -21%");
                    return true;
                }
            }

            if (_direccion == "ALCISTA")
            {
                bool toco100 = _h1Bars.LowPrices[index] <= _level100;
                bool tocoMinus21 = _h1Bars.HighPrices[index] >= _levelMinus21;

                if (toco100)
                {
                    Print("RESOLUCION: INVALIDACION ALCISTA | TOCO 100%");
                    return true;
                }

                if (tocoMinus21)
                {
                    Print("RESOLUCION: CONTINUACION ALCISTA | TOCO -21%");
                    return true;
                }
            }

            return false;
        }

        private void GestionarDibujo()
        {
            bool puedeDibujar = _estado == EstadoSwing.SwingActivo || _estado == EstadoSwing.SwingCongelado;

            if (puedeDibujar)
                DibujarSwingFibo();
            else
                BorrarObjetosSwingFibo();
        }

        private void DibujarSwingFibo()
        {
            BorrarObjetosSwingFibo();

            if (_originBar < 0 || _endBar < 0)
                return;

            Chart.DrawTrendLine(
                "SF_SWING",
                _h1Bars.OpenTimes[_originBar],
                _originPrice,
                _h1Bars.OpenTimes[_endBar],
                _endPrice,
                Color.FromArgb(120, 255, 255, 0),
                1,
                LineStyle.Solid
            );

            Color red100 = Color.FromArgb(70, 255, 0, 0);
            Color blueZone = Color.FromArgb(70, 0, 120, 255);
            Color greenTarget = Color.FromArgb(80, 0, 220, 0);
            Color graySoft = Color.FromArgb(65, 180, 180, 180);
            
            Chart.DrawHorizontalLine("SF_100", _level100, red100, 1, LineStyle.Solid);
            Chart.DrawHorizontalLine("SF_764", _level764, graySoft, 1, LineStyle.Dots);
            Chart.DrawHorizontalLine("SF_618", _level618, blueZone, 1, LineStyle.Dots);
            Chart.DrawHorizontalLine("SF_50", _level50, blueZone, 1, LineStyle.Dots);
            Chart.DrawHorizontalLine("SF_382", _level382, graySoft, 1, LineStyle.Dots);
            Chart.DrawHorizontalLine("SF_0", _level0, greenTarget, 1, LineStyle.Solid);
            Chart.DrawHorizontalLine("SF_MINUS21", _levelMinus21, greenTarget, 1, LineStyle.Dots);
        }

        private void BorrarObjetosSwingFibo()
        {
            Chart.RemoveObject("SF_SWING");
            Chart.RemoveObject("SF_100");
            Chart.RemoveObject("SF_764");
            Chart.RemoveObject("SF_618");
            Chart.RemoveObject("SF_50");
            Chart.RemoveObject("SF_382");
            Chart.RemoveObject("SF_0");
            Chart.RemoveObject("SF_MINUS21");
        }

        private void ReiniciarSwing()
        {
            _estado = EstadoSwing.BuscandoSwing;
            _direccion = "NO";

            _originBar = -1;
            _endBar = -1;
            _activatedBar = -1;

            _originPrice = 0;
            _endPrice = 0;

            _level0 = 0;
            _level382 = 0;
            _level50 = 0;
            _level618 = 0;
            _level764 = 0;
            _level100 = 0;
            _levelMinus21 = 0;

            BorrarObjetosSwingFibo();
        }

        private string GetSwingStatusText()
        {
            if (_estado == EstadoSwing.BuscandoSwing)
                return "BUSCANDO";

            if (_estado == EstadoSwing.SwingCandidato)
                return "CAND " + _direccion;

            if (_estado == EstadoSwing.SwingActivo)
                return "ACTIVO " + _direccion;

            if (_estado == EstadoSwing.SwingCongelado)
                return "CONG " + _direccion;

            return "BUSCANDO";
        }

        private string GetD1Trend(int index)
{
    // 1. MATEMÁTICA: Medimos la distancia absoluta entre la EMA8 y la EMA21
    double separation = Math.Abs(_d1Ema8.Result[index] - _d1Ema21.Result[index]);
    
    // 2. FILTRO SENSENSIBILIDAD: Umbral unificado al 14% del ATR diario
    double trendThreshold = _d1Atr20.Result[index] * 0.14;

    // 3. FILTRO LATERAL: Si están muy juntas, el mercado está plano
    if (separation < trendThreshold)
    {
        return "Lateral";
    }

    double close = _d1Bars.ClosePrices[index];

    // 4. IMPULSO VIVO: El precio cotiza libre por encima o por debajo de las 3 medias
    if (close > _d1Ema8.Result[index] &&
        close > _d1Ema21.Result[index] &&
        close > _d1Ema50.Result[index])
        return "Alcista | Check";

    if (close < _d1Ema8.Result[index] &&
        close < _d1Ema21.Result[index] &&
        close < _d1Ema50.Result[index])
        return "Bajista | Check";

    // 5. RETROCESO SANO: El precio está en medio, pero la EMA21 y la EMA50 mantienen la dirección macro
    if (_d1Ema21.Result[index] > _d1Ema50.Result[index])
        return "Retroceso Alcista | Check";

    if (_d1Ema21.Result[index] < _d1Ema50.Result[index])
        return "Retroceso Bajista | Check";

    return "Lateral";
}


        private string GetH1Trend(int index)
        {
            if (_h1Ema8.Result[index] > _h1Ema21.Result[index] &&
                _h1Ema21.Result[index] > _h1Ema50.Result[index])
                return "ALCISTA";

            if (_h1Ema8.Result[index] < _h1Ema21.Result[index] &&
                _h1Ema21.Result[index] < _h1Ema50.Result[index])
                return "BAJISTA";

            return "CONSOL";
        }

        private string GetSync(string d1Trend, string h1Trend)
        {
            if (d1Trend == "CONSOL" || h1Trend == "CONSOL")
                return "NO";

            return d1Trend == h1Trend ? "SI" : "NO";
        }

        private bool AreD1EmasAligned(int index)
        {
            bool bullish = _d1Ema8.Result[index] > _d1Ema21.Result[index] &&
                           _d1Ema21.Result[index] > _d1Ema50.Result[index];

            bool bearish = _d1Ema8.Result[index] < _d1Ema21.Result[index] &&
                           _d1Ema21.Result[index] < _d1Ema50.Result[index];

            return bullish || bearish;
        }

       private string GetD1Phase(string trend, string nearEma8, int index)
{
    // ENTORNO OBLIGATORIO: Si el mercado está en un lateral, las fases NO existen.
    if (trend.Contains("Lateral"))
    {
        return "Neutral / Ruido | Pend";
    }

    double close = _d1Bars.ClosePrices[index];

    // --- ESCENARIO DE TENDENCIA ALCISTA ---
    if (trend.Contains("Alcista"))
    {
        // F3 — SOBREEXTENSIÓN: El precio rompió y cerró por encima del último Swing High confirmado en D1
        if (close > _lastConfirmedD1SwingHigh)
        {
            return "F3 - Sobreextensión | Pend";
        }

        // F2 — CONTINUACIÓN: El precio va hacia arriba pero sigue por debajo o igual al Swing High confirmado (Retesteo)
        if (close <= _lastConfirmedD1SwingHigh && !nearEma8.Contains("Extendido") && close > _d1Ema8.Result[index])
        {
            return "F2 - Continuación | Check";
        }

        // F1 — CORRECCIÓN: El precio está en pleno retroceso comprimiéndose en la zona de valor (EMAs)
        return "F1 - Corrección | Pend";
    }

    // --- ESCENARIO DE TENDENCIA BAJISTA ---
    if (trend.Contains("Bajista"))
    {
        // F3 — SOBREEXTENSIÓN: El precio rompió y cerró por debajo del último Swing Low confirmado en D1
        if (close < _lastConfirmedD1SwingLow)
        {
            return "F3 - Sobreextensión | Pend";
        }

        // F2 — CONTINUACIÓN: El precio va cayendo hacia el suelo previo pero sigue por encima o igual al Swing Low confirmado
        if (close >= _lastConfirmedD1SwingLow && !nearEma8.Contains("Extendido") && close < _d1Ema8.Result[index])
        {
            return "F2 - Continuación | Check";
        }

        // F1 — CORRECCIÓN: El precio está en pleno rebote al alza buscando las EMAs
        return "F1 - Corrección | Pend";
    }

    return "Neutral / Ruido | Pend";
}




        private string GetD1Health(int index, string d1Trend)
        {
            double high1, high2, low1, low2;
            int highBar1, highBar2, lowBar1, lowBar2;

            bool foundHighs = FindLastTwoD1SwingHighs(index, out high1, out high2, out highBar1, out highBar2);
            bool foundLows = FindLastTwoD1SwingLows(index, out low1, out low2, out lowBar1, out lowBar2);

            if (!foundHighs || !foundLows)
                return "N/A";

            if (d1Trend == "ALCISTA")
            {
                if (high2 <= high1)
                    return "N/A";

                double ao1 = GetD1AOMaxAroundPivot(highBar1, 2);
                double ao2 = GetD1AOMaxAroundPivot(highBar2, 2);

                if (ao1 <= 0 || ao2 <= 0)
                    return "N/A";

                if (ao2 > ao1)
                    return "CONV ALC";

                if (ao2 < ao1)
                    return "DIV BAJ";
            }

            if (d1Trend == "BAJISTA")
            {
                if (low2 >= low1)
                    return "N/A";

                double ao1 = GetD1AOMinAroundPivot(lowBar1, 2);
                double ao2 = GetD1AOMinAroundPivot(lowBar2, 2);

                if (ao1 >= 0 || ao2 >= 0)
                    return "N/A";

                if (ao2 < ao1)
                    return "CONV BAJ";

                if (ao2 > ao1)
                    return "DIV ALC";
            }

            return "N/A";
        }

        private string GetD1Momentum(int index)
        {
            double ao = CalculateD1AO(index);

            if (ao > 0)
                return "ALCISTA";

            if (ao < 0)
                return "BAJISTA";

            return "NEUTRAL";
        }

        private string GetH1Structure(int index, string h1Trend)
        {
            double high1, high2, low1, low2;
            int highBar1, highBar2, lowBar1, lowBar2;

            bool foundHighs = FindLastTwoH1SwingHighs(index, out high1, out high2, out highBar1, out highBar2);
            bool foundLows = FindLastTwoH1SwingLows(index, out low1, out low2, out lowBar1, out lowBar2);

            if (!foundHighs || !foundLows)
                return "NEUTRAL";

            bool bullishStructure = low2 > low1 && high2 > high1;
            bool bearishStructure = high2 < high1 && low2 < low1;

            if (h1Trend == "ALCISTA" && bullishStructure)
                return "SANA COMPRA";

            if (h1Trend == "BAJISTA" && bearishStructure)
                return "SANA VENTA";

            return "NEUTRAL";
        }

        private string GetH1Health(int index, string h1Trend)
        {
            double high1, high2, low1, low2;
            int highBar1, highBar2, lowBar1, lowBar2;

            bool foundHighs = FindLastTwoH1SwingHighs(index, out high1, out high2, out highBar1, out highBar2);
            bool foundLows = FindLastTwoH1SwingLows(index, out low1, out low2, out lowBar1, out lowBar2);

            if (!foundHighs || !foundLows)
                return "N/A";

            if (h1Trend == "ALCISTA")
            {
                if (high2 <= high1)
                    return "N/A";

                double ao1 = GetH1AOMaxAroundPivot(highBar1, 2);
                double ao2 = GetH1AOMaxAroundPivot(highBar2, 2);

                if (ao1 <= 0 || ao2 <= 0)
                    return "N/A";

                if (ao2 > ao1)
                    return "CONV ALC";

                if (ao2 < ao1)
                    return "DIV BAJ";
            }

            if (h1Trend == "BAJISTA")
            {
                if (low2 >= low1)
                    return "N/A";

                double ao1 = GetH1AOMinAroundPivot(lowBar1, 2);
                double ao2 = GetH1AOMinAroundPivot(lowBar2, 2);

                if (ao1 >= 0 || ao2 >= 0)
                    return "N/A";

                if (ao2 < ao1)
                    return "CONV BAJ";

                if (ao2 > ao1)
                    return "DIV ALC";
            }

            return "N/A";
        }

        private string GetH1Momentum(int index)
        {
            double ao0 = CalculateH1AO(index - 4);
            double ao1 = CalculateH1AO(index - 3);
            double ao2 = CalculateH1AO(index - 2);
            double ao3 = CalculateH1AO(index - 1);
            double ao4 = CalculateH1AO(index);

            bool allPositive = ao0 > 0 && ao1 > 0 && ao2 > 0 && ao3 > 0 && ao4 > 0;
            bool allNegative = ao0 < 0 && ao1 < 0 && ao2 < 0 && ao3 < 0 && ao4 < 0;

            bool increasing = ao1 > ao0 && ao2 > ao1 && ao3 > ao2 && ao4 > ao3;
            bool decreasing = ao1 < ao0 && ao2 < ao1 && ao3 < ao2 && ao4 < ao3;

            int positiveReductions = 0;
            if (ao1 < ao0) positiveReductions++;
            if (ao2 < ao1) positiveReductions++;
            if (ao3 < ao2) positiveReductions++;
            if (ao4 < ao3) positiveReductions++;

            int negativeWeakening = 0;
            if (ao1 > ao0) negativeWeakening++;
            if (ao2 > ao1) negativeWeakening++;
            if (ao3 > ao2) negativeWeakening++;
            if (ao4 > ao3) negativeWeakening++;

            if (allPositive && positiveReductions >= 3)
                return "AGOT ALC";

            if (allNegative && negativeWeakening >= 3)
                return "AGOT BAJ";

            if (allPositive && increasing)
                return "ALC CREC";

            if (allPositive && decreasing)
                return "ALC DECR";

            if (allNegative && decreasing)
                return "BAJ CREC";

            if (allNegative && increasing)
                return "BAJ DECR";

            return "NEUTRAL";
        }

        private double GetD1AOMaxAroundPivot(int pivotIndex, int window)
        {
            double max = double.MinValue;
            int start = Math.Max(34, pivotIndex - window);
            int end = Math.Min(_d1Bars.Count - 2, pivotIndex + window);

            for (int i = start; i <= end; i++)
            {
                double ao = CalculateD1AO(i);
                if (ao > max)
                    max = ao;
            }

            return max;
        }

        private double GetD1AOMinAroundPivot(int pivotIndex, int window)
        {
            double min = double.MaxValue;
            int start = Math.Max(34, pivotIndex - window);
            int end = Math.Min(_d1Bars.Count - 2, pivotIndex + window);

            for (int i = start; i <= end; i++)
            {
                double ao = CalculateD1AO(i);
                if (ao < min)
                    min = ao;
            }

            return min;
        }

        private double GetH1AOMaxAroundPivot(int pivotIndex, int window)
        {
            double max = double.MinValue;
            int start = Math.Max(34, pivotIndex - window);
            int end = Math.Min(_h1Bars.Count - 2, pivotIndex + window);

            for (int i = start; i <= end; i++)
            {
                double ao = CalculateH1AO(i);
                if (ao > max)
                    max = ao;
            }

            return max;
        }

        private double GetH1AOMinAroundPivot(int pivotIndex, int window)
        {
            double min = double.MaxValue;
            int start = Math.Max(34, pivotIndex - window);
            int end = Math.Min(_h1Bars.Count - 2, pivotIndex + window);

            for (int i = start; i <= end; i++)
            {
                double ao = CalculateH1AO(i);
                if (ao < min)
                    min = ao;
            }

            return min;
        }

        private double CalculateD1AO(int index)
        {
            return D1MedianSma(index, 5) - D1MedianSma(index, 34);
        }

        private double CalculateH1AO(int index)
        {
            return H1MedianSma(index, 5) - H1MedianSma(index, 34);
        }

        private double D1MedianSma(int index, int period)
        {
            double sum = 0;

            for (int i = index - period + 1; i <= index; i++)
            {
                double median = (_d1Bars.HighPrices[i] + _d1Bars.LowPrices[i]) / 2.0;
                sum += median;
            }

            return sum / period;
        }

        private double H1MedianSma(int index, int period)
        {
            double sum = 0;

            for (int i = index - period + 1; i <= index; i++)
            {
                double median = (_h1Bars.HighPrices[i] + _h1Bars.LowPrices[i]) / 2.0;
                sum += median;
            }

            return sum / period;
        }

        private bool FindLastTwoD1SwingHighs(int currentIndex, out double high1, out double high2, out int highBar1, out int highBar2)
        {
            high1 = high2 = 0;
            highBar1 = highBar2 = -1;
            int found = 0;

            for (int i = currentIndex - PivotStrength; i >= PivotStrength + 34; i--)
            {
                if (IsD1SwingHigh(i, PivotStrength))
                {
                    if (found == 0)
                    {
                        high2 = _d1Bars.HighPrices[i];
                        highBar2 = i;
                        found++;
                    }
                    else
                    {
                        high1 = _d1Bars.HighPrices[i];
                        highBar1 = i;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool FindLastTwoD1SwingLows(int currentIndex, out double low1, out double low2, out int lowBar1, out int lowBar2)
        {
            low1 = low2 = 0;
            lowBar1 = lowBar2 = -1;
            int found = 0;

            for (int i = currentIndex - PivotStrength; i >= PivotStrength + 34; i--)
            {
                if (IsD1SwingLow(i, PivotStrength))
                {
                    if (found == 0)
                    {
                        low2 = _d1Bars.LowPrices[i];
                        lowBar2 = i;
                        found++;
                    }
                    else
                    {
                        low1 = _d1Bars.LowPrices[i];
                        lowBar1 = i;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool FindLastTwoH1SwingHighs(int currentIndex, out double high1, out double high2, out int highBar1, out int highBar2)
        {
            high1 = high2 = 0;
            highBar1 = highBar2 = -1;
            int found = 0;

            for (int i = currentIndex - PivotStrength; i >= PivotStrength + 34; i--)
            {
                if (IsH1SwingHigh(i, PivotStrength))
                {
                    if (found == 0)
                    {
                        high2 = _h1Bars.HighPrices[i];
                        highBar2 = i;
                        found++;
                    }
                    else
                    {
                        high1 = _h1Bars.HighPrices[i];
                        highBar1 = i;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool FindLastTwoH1SwingLows(int currentIndex, out double low1, out double low2, out int lowBar1, out int lowBar2)
        {
            low1 = low2 = 0;
            lowBar1 = lowBar2 = -1;
            int found = 0;

            for (int i = currentIndex - PivotStrength; i >= PivotStrength + 34; i--)
            {
                if (IsH1SwingLow(i, PivotStrength))
                {
                    if (found == 0)
                    {
                        low2 = _h1Bars.LowPrices[i];
                        lowBar2 = i;
                        found++;
                    }
                    else
                    {
                        low1 = _h1Bars.LowPrices[i];
                        lowBar1 = i;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsD1SwingHigh(int index, int strength)
        {
            double high = _d1Bars.HighPrices[index];

            for (int i = 1; i <= strength; i++)
            {
                if (_d1Bars.HighPrices[index - i] >= high)
                    return false;

                if (_d1Bars.HighPrices[index + i] >= high)
                    return false;
            }

            return true;
        }

        private bool IsD1SwingLow(int index, int strength)
        {
            double low = _d1Bars.LowPrices[index];

            for (int i = 1; i <= strength; i++)
            {
                if (_d1Bars.LowPrices[index - i] <= low)
                    return false;

                if (_d1Bars.LowPrices[index + i] <= low)
                    return false;
            }

            return true;
        }

        private bool IsH1SwingHigh(int index, int strength)
        {
            double high = _h1Bars.HighPrices[index];

            for (int i = 1; i <= strength; i++)
            {
                if (high <= _h1Bars.HighPrices[index - i])
                    return false;

                if (high <= _h1Bars.HighPrices[index + i])
                    return false;
            }

            return true;
        }

        private bool IsH1SwingLow(int index, int strength)
        {
            double low = _h1Bars.LowPrices[index];

            for (int i = 1; i <= strength; i++)
            {
                if (low >= _h1Bars.LowPrices[index - i])
                    return false;

                if (low >= _h1Bars.LowPrices[index + i])
                    return false;
            }

            return true;
        }

        private void DrawTextPanel(string text)
        {
            Chart.DrawStaticText(
                "SF_DASHBOARD",
                text,
                VerticalAlignment.Top,
                HorizontalAlignment.Left,
                Color.FromArgb(160, 160, 160, 160)
            );
        }
    
    private string GetD1EmasAlineacionStatus(int index)
{
    // 1. MATEMÁTICA: Calculamos las líneas base de tus medias en D1
    double ema8 = _d1Ema8.Result[index];
    double ema21 = _d1Ema21.Result[index];
    double ema50 = _d1Ema50.Result[index];

    // 2. FILTRO: Calculamos el umbral del 15% del ATR de D1 para medir la separación real
    double separationThreshold = _d1Atr20.Result[index] * 0.15;
    double currentSeparation = Math.Abs(ema21 - ema50);

    // 3. VALIDACIÓN GEOMÉTRICA ALCISTA (8 > 21 > 50)
    if (ema8 > ema21 && ema21 > ema50)
    {
        if (currentSeparation >= separationThreshold)
            return "Alineadas | Check";
        else
            return "En Carga | Pend";
    }

    // 4. VALIDACIÓN GEOMÉTRICA BAJISTA (8 < 21 < 50)
    if (ema8 < ema21 && ema21 < ema50)
    {
        if (currentSeparation >= separationThreshold)
            return "Alineadas | Check";
        else
            return "En Carga | Pend";
    }

    // 5. CUALQUIER OTRO ORDEN SIGNIFICA QUE ESTÁN TRENZADAS
    return "Cruzadas | Pend";
}    
    }
}
