using cAlgo.API;
using cAlgo.API.Indicators;
using System;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class SimpleFlowDashboard_v20 : Robot
    {
        [Parameter("Carril 8 ATR Threshold", DefaultValue = 0.45, MinValue = 0.20, MaxValue = 0.75, Step = 0.05)]
public double Ema8ProximityThreshold { get; set; }

        [Parameter("Trend Min Separation (%)", DefaultValue = 7.0, MinValue = 0.0, MaxValue = 100.0, Step = 1.0)]
        public double TrendMinSeparationPercent { get; set; }

        [Parameter("Trend Check Separation (%)", DefaultValue = 14.0, MinValue = 0.0, MaxValue = 100.0, Step = 1.0)]
        public double TrendCheckSeparationPercent { get; set; }

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

        private DirectionalMovementSystem _d1Dms14;
        private DirectionalMovementSystem _h1Dms14;

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
        private double _lastConfirmedD1SwingHigh = double.NaN;
        private double _lastConfirmedD1SwingLow = double.NaN;

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

            _d1Dms14 = Indicators.DirectionalMovementSystem(_d1Bars, 14);
            _h1Dms14 = Indicators.DirectionalMovementSystem(_h1Bars, 14);

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
            double d1AtrPips = d1Atr / Symbol.PipSize;
            double h1AtrPips = h1Atr / Symbol.PipSize;

            double atrPercent = (d1Atr > 0) ? (h1Atr / d1Atr) * 100 : 0;

            string atrStatus;

if (atrPercent <= 15.0)
{
    atrStatus = "Compresion";
}
else if (atrPercent <= 25.0)
{
    atrStatus = "Rango normal";
}
else if (atrPercent <= 40.0)
{
    atrStatus = "Expansion";
}
else
{
    atrStatus = "Expansion fuerte";
}

             double distEma8 = Math.Abs(d1Close - d1Ema8);
double maxDist = d1Atr * Ema8ProximityThreshold;
bool d1InEma8Range = distEma8 <= maxDist;

string d1Trend = GetD1Trend(d1Index);
string d1NearEma8 = d1InEma8Range
    ? "En Rango | Check"
    : "Fuera de Rango | Pend";

            string d1EmasAligned = GetD1EmasAlineacionStatus(d1Index);
            string d1Directional = GetDirectionalStatus(_d1Dms14, d1Index, d1Trend);
                                // 4. ESTRUCTURA CONGELADA D1: El robot procesa los pivotes en segundo plano
            ProcesarSwingFiboD1(d1Index);
            string d1Phase = GetD1Phase(d1Trend, d1Index);
            string d1Health = GetD1Health(d1Index, d1Trend);
            string d1Momentum = GetD1Momentum(d1Index);

            string h1Trend = GetH1Trend(h1Index);
            string h1Directional = GetDirectionalStatus(_h1Dms14, h1Index, h1Trend);
            string sync = GetSync(d1Trend, h1Trend);
            string h1TrendDisplay =
               h1Trend == "ALCISTA" ? "Alcista" :
               h1Trend == "BAJISTA" ? "Bajista" :
                "Sin Tendencia";
            string h1Structure = GetH1Structure(h1Index, h1Trend);
            string h1Health = GetH1Health(h1Index, h1Trend);
            string h1Momentum = GetH1Momentum(h1Index);
            string swingStatus = GetSwingStatusText();

        
           string dashboard =
$@"D1 ATR20 : {d1AtrPips:F1}
H1 ATR20 : {h1AtrPips:F1} ({atrPercent:F0}%) - {atrStatus}

--- D1 ---
Trend      : {d1Trend}
DI14       : {d1Directional}
EMAs       : {d1EmasAligned}
Carril 8   : {d1NearEma8}
Phase      : {d1Phase}
Fuerza MKD : {d1Health}
Mom        : {d1Momentum}

--- H1 ---
Trend      : {h1TrendDisplay} | Sync {sync}
DI14       : {h1Directional}
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

        private string GetDirectionalStatus(
    DirectionalMovementSystem dms,
    int index,
    string trend)
{
    double diPlus = dms.DIPlus[index];
    double diMinus = dms.DIMinus[index];
    double adx = dms.ADX[index];

    if (Math.Abs(diPlus - diMinus) < 3.0)
        return "Neutral | Sin Señal";

    bool diBullish = diPlus > diMinus;
    bool trendBullish = trend.Contains("Alcista") || trend == "ALCISTA";
    bool trendBearish = trend.Contains("Bajista") || trend == "BAJISTA";

    string movement;

    if (trendBullish)
        movement = diBullish ? "Push Alcista" : "Pullback Bajista";
    else if (trendBearish)
        movement = diBullish ? "Pullback Alcista" : "Push Bajista";
    else
        movement = diBullish ? "Push Alcista" : "Push Bajista";

    string status;

    if (adx < 20.0)
        status = "Débil";
    else if (adx < 25.0)
        status = "En Desarrollo";
    else if (adx < 40.0)
        status = "Activo";
    else if (adx < 60.0)
        status = "Fuerte";
    else
        status = "Fuerte XL";

    return movement + " | " + status;
}

      private string GetD1Trend(int index)
{
    double ema21 = _d1Ema21.Result[index];
    double ema50 = _d1Ema50.Result[index];
    double atr = _d1Atr20.Result[index];

    double minPercent = Math.Min(
        TrendMinSeparationPercent,
        TrendCheckSeparationPercent);

    double checkPercent = Math.Max(
        TrendMinSeparationPercent,
        TrendCheckSeparationPercent);

    double separation = Math.Abs(ema21 - ema50);
    double minThreshold = atr * (minPercent / 100.0);
    double checkThreshold = atr * (checkPercent / 100.0);

    if (atr <= 0 || separation < minThreshold)
        return "Sin Tendencia | Pend";

    string direction = ema21 > ema50 ? "Alcista" : "Bajista";

    if (separation < checkThreshold)
        return direction + " | En Desarrollo";

    return direction + " | Check";
}


        private string GetH1Trend(int index)
{
    double ema21 = _h1Ema21.Result[index];
    double ema50 = _h1Ema50.Result[index];
    double atr = _h1Atr20.Result[index];

    double minPercent = Math.Min(
        TrendMinSeparationPercent,
        TrendCheckSeparationPercent);

    double separation = Math.Abs(ema21 - ema50);
    double minThreshold = atr * (minPercent / 100.0);

    if (atr <= 0 || separation < minThreshold)
        return "CONSOL";

    return ema21 > ema50 ? "ALCISTA" : "BAJISTA";
}

        private string GetSync(string d1Trend, string h1Trend)
{
    bool d1Bullish = d1Trend.Contains("Alcista");
    bool d1Bearish = d1Trend.Contains("Bajista");

    if (d1Bullish && h1Trend == "ALCISTA")
        return "On";

    if (d1Bearish && h1Trend == "BAJISTA")
        return "On";

    return "Off";
}

        private bool AreD1EmasAligned(int index)
        {
            bool bullish = _d1Ema8.Result[index] > _d1Ema21.Result[index] &&
                           _d1Ema21.Result[index] > _d1Ema50.Result[index];

            bool bearish = _d1Ema8.Result[index] < _d1Ema21.Result[index] &&
                           _d1Ema21.Result[index] < _d1Ema50.Result[index];

            return bullish || bearish;
        }

       private string GetD1Phase(string trend, int index)
{
    if (!trend.Contains("| Check"))
        return "No Aplica | Pend";

    double close = _d1Bars.ClosePrices[index];
    double ema8 = _d1Ema8.Result[index];

    if (trend.Contains("Alcista"))
    {
        if (double.IsNaN(_lastConfirmedD1SwingHigh))
            return "Sin Referencia | Pend";

        if (close <= ema8)
            return "F1 - Corrección | Pend";

        if (close <= _lastConfirmedD1SwingHigh)
            return "F2 - Continuación | Check";

        return "F3 - Expansión | Pend";
    }

    if (trend.Contains("Bajista"))
    {
        if (double.IsNaN(_lastConfirmedD1SwingLow))
            return "Sin Referencia | Pend";

        if (close >= ema8)
            return "F1 - Corrección | Pend";

        if (close >= _lastConfirmedD1SwingLow)
            return "F2 - Continuación | Check";

        return "F3 - Expansión | Pend";
    }

    return "No Aplica | Pend";
}

 private void ProcesarSwingFiboD1(int index)
{
    // 1. CONTROL TEMPORAL: Necesitamos al menos 3 barras diarias cerradas para evaluar un pivote
    if (index < 3) return;

    // 2. RASTREO: Buscamos hacia atrás el último giro estructural confirmado en el gráfico diario
    for (int i = 2; i < 150; i++)
    {
        int checkIndex = index - i;
        if (checkIndex < 2) break;

        // --- DETECCIÓN DE SWING HIGH (TECHO CONGELADO) ---
        if (_d1Bars.HighPrices[checkIndex] > _d1Bars.HighPrices[checkIndex - 1] &&
            _d1Bars.HighPrices[checkIndex] > _d1Bars.HighPrices[checkIndex + 1] &&
            _d1Bars.HighPrices[checkIndex] > _d1Bars.HighPrices[checkIndex - 2] &&
            _d1Bars.HighPrices[checkIndex] > _d1Bars.HighPrices[checkIndex + 2])
        {
            if (_d1Ema8.Result[checkIndex] > _d1Ema21.Result[checkIndex] && 
                _d1Ema21.Result[checkIndex] > _d1Ema50.Result[checkIndex])
            {
                _lastConfirmedD1SwingHigh = _d1Bars.HighPrices[checkIndex];
                break;
            }
        }
    }

    for (int i = 2; i < 150; i++)
    {
        int checkIndex = index - i;
        if (checkIndex < 2) break;

        // --- DETECCIÓN DE SWING LOW (SUELO CONGELADO) ---
        if (_d1Bars.LowPrices[checkIndex] < _d1Bars.LowPrices[checkIndex - 1] &&
            _d1Bars.LowPrices[checkIndex] < _d1Bars.LowPrices[checkIndex + 1] &&
            _d1Bars.LowPrices[checkIndex] < _d1Bars.LowPrices[checkIndex - 2] &&
            _d1Bars.LowPrices[checkIndex] < _d1Bars.LowPrices[checkIndex + 2])
        {
            if (_d1Ema8.Result[checkIndex] < _d1Ema21.Result[checkIndex] && 
                _d1Ema21.Result[checkIndex] < _d1Ema50.Result[checkIndex])
            {
                _lastConfirmedD1SwingLow = _d1Bars.LowPrices[checkIndex];
                break;
            }
        }
    }
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
    double ema8 = _d1Ema8.Result[index];
    double ema21 = _d1Ema21.Result[index];
    double ema50 = _d1Ema50.Result[index];
    double atr = _d1Atr20.Result[index];

    if (atr <= 0)
        return "N/A";

    double minPercent = Math.Min(
        TrendMinSeparationPercent,
        TrendCheckSeparationPercent);

    double checkPercent = Math.Max(
        TrendMinSeparationPercent,
        TrendCheckSeparationPercent);

    double minThreshold = atr * (minPercent / 100.0);
    double checkThreshold = atr * (checkPercent / 100.0);

    double macroSeparation = Math.Abs(ema21 - ema50);
    double fastSeparation = Math.Abs(ema8 - ema21);

    if (macroSeparation < minThreshold)
        return "Transición | Pend";

    bool bullishStructure = ema21 > ema50;
    bool bearishStructure = ema21 < ema50;

    if (bullishStructure)
    {
        if (ema8 > ema21)
        {
            if (macroSeparation >= checkThreshold &&
                fastSeparation >= minThreshold)
                return "Alineadas | Check";

            return "En Carga | Pend";
        }

        if (ema8 > ema50)
            return "Repliegue | Pend";

        return "Repliegue Profundo | Pend";
    }

    if (bearishStructure)
    {
        if (ema8 < ema21)
        {
            if (macroSeparation >= checkThreshold &&
                fastSeparation >= minThreshold)
                return "Alineadas | Check";

            return "En Carga | Pend";
        }

        if (ema8 < ema50)
            return "Repliegue | Pend";

        return "Repliegue Profundo | Pend";
    }

    return "Transición | Pend";
}
    }
}
