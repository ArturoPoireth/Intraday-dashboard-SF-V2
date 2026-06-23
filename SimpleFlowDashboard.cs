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

        [Parameter("AO Difference Tolerance (%)", DefaultValue = 5.0, MinValue = 0.0, MaxValue = 25.0, Step = 1.0)]
        public double AoDifferenceTolerancePercent { get; set; }

        [Parameter("AO Max Pivot Age D1", DefaultValue = 60, MinValue = 5, MaxValue = 250, Step = 5)]
        public int AoMaxPivotAgeD1 { get; set; }

        [Parameter("AO Max Pivot Age H1", DefaultValue = 120, MinValue = 10, MaxValue = 500, Step = 10)]
        public int AoMaxPivotAgeH1 { get; set; }

        [Parameter("Pivot Strength", DefaultValue = 3)]
        public int PivotStrength { get; set; }

        [Parameter("Min Swing ATR (%)", DefaultValue = 50.0, MinValue = 20.0, MaxValue = 150.0, Step = 5.0)]
        public double MinSwingAtrPercent { get; set; }

        [Parameter("Max Swing Life Bars", DefaultValue = 36)]
        public int MaxSwingLifeBars { get; set; }

        [Parameter("Max Frozen Life Bars", DefaultValue = 24, MinValue = 6, MaxValue = 120, Step = 1)]
        public int MaxFrozenLifeBars { get; set; }

        [Parameter("Max Candidate Life Bars", DefaultValue = 12, MinValue = 3, MaxValue = 48, Step = 1)]
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

        private enum SyncState
        {
            Off,
            ZonaConfirmacion,
            On
        }

        private enum MarketDirection
        {
             None,
             Alcista,
             Bajista
        }

        private enum FiltersState
        {
              SinSetup,
              DarSeguimiento,
              AltaPrioridad,
              SetupOptimo
        }

        private enum EstadoSwing
        {
            BuscandoSwing,
            SwingCandidato,
            SwingActivo,
            SwingCongelado
        }

        private EstadoSwing _estado = EstadoSwing.BuscandoSwing;

        private bool _h1PullbackValidated = false;
        private MarketDirection _h1PullbackDirection = MarketDirection.None;

        private string _direccion = "NO";

        private int _originBar = -1;
        private int _endBar = -1;
        private int _activatedBar = -1;
        private int _frozenBar = -1;
        private int _lastProcessedH1Index = -1;
        // --- MEMORIA ESTRUCTURAL CONGELADA D1 ---
        private double _lastConfirmedD1SwingHigh = double.NaN;
        private double _lastConfirmedD1SwingLow = double.NaN;

        private double _originPrice = 0;
        private double _endPrice = 0;
        private double _candidateBosLevel = double.NaN;

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

         if (index == _lastProcessedH1Index)
            {
              DrawDashboard();
        return;
            }
_lastProcessedH1Index = index;

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
            
            string h1Trend = GetH1Trend(h1Index);
            string h1Directional = GetDirectionalStatus(_h1Dms14, h1Index, h1Trend);
            SyncState syncState = GetSyncState(
                                  d1Trend,
                                  h1Trend,
                                  h1Index);

string sync = GetSyncDisplayText(syncState);
            string h1TrendDisplay =
               h1Trend == "ALCISTA" ? "Alcista" :
               h1Trend == "BAJISTA" ? "Bajista" :
                "Sin Tendencia";
            string h1Health = GetH1Health(h1Index, h1Trend, syncState);
            
bool d1TrendConfirmed =
    d1Trend.Contains("| Check");

bool d1EmasConfirmed =
    d1EmasAligned == "Alineadas | Check";

bool d1CarrilConfirmed =
    d1InEma8Range;

bool d1PhaseConfirmed =
    d1Phase.StartsWith("F2 -") &&
    d1Phase.Contains("| Check");

bool d1Ready =
    d1TrendConfirmed &&
    d1EmasConfirmed &&
    d1CarrilConfirmed &&
    d1PhaseConfirmed;

bool h1IndicatorConfirmed =
    h1Health.Contains("Convergencia") &&
    h1Health.Contains("| Check");

bool h1SwingReady =
    _estado == EstadoSwing.SwingActivo ||
    _estado == EstadoSwing.SwingCongelado;

    bool h1SwingOperational =
    h1SwingReady &&
    d1Ready &&
    syncState != SyncState.Off;

string swingStatus = GetSwingStatusText();

if ((_estado == EstadoSwing.SwingActivo ||
     _estado == EstadoSwing.SwingCongelado) &&
    !h1SwingOperational)
{
    string swingDirection =
        _direccion == "ALCISTA" ? "Alcista" : "Bajista";

    swingStatus = "Impulso " + swingDirection + " | Info";
}
else if (h1SwingOperational)
{
    swingStatus += " | Check";
}
else if (_estado == EstadoSwing.SwingCandidato)
{
    swingStatus += " | Info";
}




UpdateH1PullbackMemory(
    d1Ready,
    syncState,
    h1Trend,
    h1Index,
    h1SwingReady);

int filtersScore = CalculateFiltersScore(
    d1TrendConfirmed,
    d1EmasConfirmed,
    d1CarrilConfirmed,
    d1PhaseConfirmed,
    syncState,
    h1IndicatorConfirmed,
    h1SwingReady,
    _h1PullbackValidated);

FiltersState filtersState =
    GetFiltersState(filtersScore);

string filtersStateText =
    GetFiltersStateText(filtersState);


        
           string dashboard =
$@"D1 ATR20 : {d1AtrPips:F1}
H1 ATR20 : {h1AtrPips:F1} ({atrPercent:F0}%) - {atrStatus}
Filtros  : {filtersScore}% | {filtersStateText}

--- D1 ---
Trend     : {d1Trend}
EMAs      : {d1EmasAligned}
Carril 8  : {d1NearEma8}
Phase     : {d1Phase}
Indicador : {d1Health}

--- H1 ---
Trend     : {h1TrendDisplay} | Sync {sync}
Indicador : {h1Health}
Swing/Fibo: {swingStatus}
DI14      : {h1Directional}";

            DrawTextPanel(dashboard);
        }

        private void ProcesarSwingFibo(int index)
        {
            int candidate = index - PivotStrength;

        int d1Index = _d1Bars.Count - 2;
string d1Trend = GetD1Trend(d1Index);

bool contextoAlcista =
    d1Trend.Contains("Alcista") &&
    d1Trend.Contains("| Check");

bool contextoBajista =
    d1Trend.Contains("Bajista") &&
    d1Trend.Contains("| Check");

            bool pivotLow = IsH1SwingLow(candidate, PivotStrength);
            bool pivotHigh = IsH1SwingHigh(candidate, PivotStrength);
            bool swingEnProceso =
    _estado == EstadoSwing.SwingCandidato ||
    _estado == EstadoSwing.SwingActivo ||
    _estado == EstadoSwing.SwingCongelado;

bool origenRoto =
    (_direccion == "ALCISTA" &&
     _h1Bars.ClosePrices[index] < _originPrice) ||
    (_direccion == "BAJISTA" &&
     _h1Bars.ClosePrices[index] > _originPrice);

if (swingEnProceso && origenRoto)
{
    Print("SWING INVALIDADO: cierre H1 rompió el origen estructural.");
    ReiniciarSwing();
    return;
}

            if (_estado == EstadoSwing.BuscandoSwing)
            {
                if (contextoBajista && pivotHigh)
    CrearCandidatoBajista(candidate);
else if (contextoAlcista && pivotLow)
    CrearCandidatoAlcista(candidate);
            }

            else if (_estado == EstadoSwing.SwingCandidato)
            {
                ActualizarFinDinamico(index);
                CalcularNiveles();

                int candidateAge = index - (_originBar + PivotStrength);
                double range = Math.Abs(_originPrice - _endPrice);
                double minSwingRange = _h1Atr20.Result[index] * (MinSwingAtrPercent / 100.0);
                bool bosConfirmed =
                 !double.IsNaN(_candidateBosLevel) &&
                 ((_direccion == "ALCISTA" &&
                    _h1Bars.ClosePrices[index] > _candidateBosLevel) ||
                  (_direccion == "BAJISTA" &&
                   _h1Bars.ClosePrices[index] < _candidateBosLevel));
                bool swingTieneMasDeUnaVela = _originBar != _endBar;

                if (candidateAge > MaxCandidateLifeBars)
                {
                    Print("SWING CANDIDATO EXPIRADO: no alcanzó rango mínimo dentro del tiempo permitido.");
                    ReiniciarSwing();
                    return;
                }

                if (range >= minSwingRange &&
                     swingTieneMasDeUnaVela &&
                     bosConfirmed)
                {
                    _estado = EstadoSwing.SwingActivo;
                    _activatedBar = index;
                    Print("SWING ACTIVO: rango mínimo validado y más de una vela.");
                }
            }

            else if (_estado == EstadoSwing.SwingActivo)
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

                if (index > _endBar && TocoZonaPullback50(index))
                {
                    _estado = EstadoSwing.SwingCongelado;
                    _frozenBar = index;
                    Print("SWING CONGELADO: precio tocó 50%.");
                }
            }

            else if (_estado == EstadoSwing.SwingCongelado)
            {
            int frozenAge = index - _frozenBar;

            if (_frozenBar >= 0 && frozenAge > MaxFrozenLifeBars)
{
            Print("SWING CONGELADO EXPIRADO: superó {0} barras.", MaxFrozenLifeBars);
            ReiniciarSwing();
                     return;
}
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
           double previousLow;

            if (!TryGetPreviousH1SwingLow(bar, out previousLow))
            return;

            _candidateBosLevel = previousLow;

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
            double previousHigh;

        if (!TryGetPreviousH1SwingHigh(bar, out previousHigh))
          return;

         _candidateBosLevel = previousHigh;
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

            _h1PullbackValidated = false;
            _h1PullbackDirection = MarketDirection.None;

            _originBar = -1;
            _endBar = -1;
            _activatedBar = -1;
            _frozenBar = -1;

            _originPrice = 0;
            _endPrice = 0;
            _candidateBosLevel = double.NaN;

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
    string directionText =
        _direccion == "ALCISTA" ? "Alcista" :
        _direccion == "BAJISTA" ? "Bajista" :
        "";

    if (_estado == EstadoSwing.BuscandoSwing)
        return "Buscando";

    if (_estado == EstadoSwing.SwingCandidato)
        return "Candidato " + directionText;

    if (_estado == EstadoSwing.SwingActivo)
        return "Activo " + directionText;

    if (_estado == EstadoSwing.SwingCongelado)
        return "Congelado " + directionText;

    return "Buscando";
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

private SyncState GetSyncState(
    string d1Trend,
    string h1Trend,
    int h1Index)
{
    if (!d1Trend.Contains("| Check"))
        return SyncState.Off;

    bool sameBullishDirection =
        d1Trend.Contains("Alcista") &&
        h1Trend == "ALCISTA";

    bool sameBearishDirection =
        d1Trend.Contains("Bajista") &&
        h1Trend == "BAJISTA";

    if (!sameBullishDirection && !sameBearishDirection)
        return SyncState.Off;

    double atr = _h1Atr20.Result[h1Index];

    if (atr <= 0)
        return SyncState.Off;

    double separation = Math.Abs(
        _h1Ema21.Result[h1Index] -
        _h1Ema50.Result[h1Index]);

    double checkPercent = Math.Max(
        TrendMinSeparationPercent,
        TrendCheckSeparationPercent);

    double checkThreshold =
        atr * (checkPercent / 100.0);

    if (separation >= checkThreshold)
        return SyncState.On;

    return SyncState.ZonaConfirmacion;
}
    private string GetSyncDisplayText(SyncState syncState)
{
    if (syncState == SyncState.On)
        return "On";

    if (syncState == SyncState.ZonaConfirmacion)
        return "Zona de Confirmación";

    return "Off";
}
 
private string GetFiltersStateText(FiltersState filtersState)
{

    if (filtersState == FiltersState.SetupOptimo)
        return "Setup Óptimo | Esperar Gatillo";

    if (filtersState == FiltersState.AltaPrioridad)
        return "Alta Prioridad";

    if (filtersState == FiltersState.DarSeguimiento)
        return "Dar Seguimiento";

    return "Sin Setup";
}

private int CalculateFiltersScore(
    bool d1TrendConfirmed,
    bool d1EmasConfirmed,
    bool d1CarrilConfirmed,
    bool d1PhaseConfirmed,
    SyncState syncState,
    bool h1IndicatorConfirmed,
    bool h1SwingReady,
    bool h1PullbackValidated)
{
    int score = 0;

    if (d1TrendConfirmed)
        score += 10;

    if (d1EmasConfirmed)
        score += 10;

    if (d1CarrilConfirmed)
        score += 5;

    if (d1PhaseConfirmed)
        score += 25;

    bool d1Ready =
        d1TrendConfirmed &&
        d1EmasConfirmed &&
        d1CarrilConfirmed &&
        d1PhaseConfirmed;

    if (!d1Ready)
        return score;

    if (syncState == SyncState.On)
        score += 15;
    else if (syncState == SyncState.ZonaConfirmacion)
        score += 10;
    else
        return score;

    if (h1IndicatorConfirmed)
        score += 15;

    if (h1SwingReady)
        score += 10;

    if (h1PullbackValidated)
        score += 10;

    return score;
}

private FiltersState GetFiltersState(int score)
{
    if (score >= 100)
        return FiltersState.SetupOptimo;

    if (score >= 80)
        return FiltersState.AltaPrioridad;

    if (score >= 50)
        return FiltersState.DarSeguimiento;

    return FiltersState.SinSetup;
}

private MarketDirection GetH1MarketDirection(string h1Trend)
{
    if (h1Trend == "ALCISTA")
        return MarketDirection.Alcista;

    if (h1Trend == "BAJISTA")
        return MarketDirection.Bajista;

    return MarketDirection.None;
}

private bool IsH1Pullback(
    int h1Index,
    MarketDirection direction)
{
    double diPlus = _h1Dms14.DIPlus[h1Index];
    double diMinus = _h1Dms14.DIMinus[h1Index];

    if (Math.Abs(diPlus - diMinus) < 3.0)
        return false;

    if (direction == MarketDirection.Alcista)
        return diMinus > diPlus;

    if (direction == MarketDirection.Bajista)
        return diPlus > diMinus;

    return false;
}

private void UpdateH1PullbackMemory(
    bool d1Ready,
    SyncState syncState,
    string h1Trend,
    int h1Index,
    bool h1SwingReady)
{
    MarketDirection currentDirection =
        GetH1MarketDirection(h1Trend);

    bool mustReset =
        !d1Ready ||
        syncState == SyncState.Off ||
        currentDirection == MarketDirection.None ||
        !h1SwingReady;

    if (mustReset)
    {
        _h1PullbackValidated = false;
        _h1PullbackDirection = MarketDirection.None;
        return;
    }

    if (_h1PullbackDirection != MarketDirection.None &&
        _h1PullbackDirection != currentDirection)
    {
        _h1PullbackValidated = false;
        _h1PullbackDirection = MarketDirection.None;
    }

    if (IsH1Pullback(h1Index, currentDirection))
    {
        _h1PullbackValidated = true;
        _h1PullbackDirection = currentDirection;
    }
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
 
 private bool IsAoDifferenceSignificant(double ao1, double ao2)
{
    double referenceValue = Math.Max(
        Math.Abs(ao1),
        Math.Abs(ao2));

    if (referenceValue <= double.Epsilon)
        return false;

    double differencePercent =
        Math.Abs(ao2 - ao1) / referenceValue * 100.0;

    return differencePercent >= AoDifferenceTolerancePercent;
}
    private string GetD1Health(int index, string d1Trend)
{
    bool trendBullish = d1Trend.Contains("Alcista");
    bool trendBearish = d1Trend.Contains("Bajista");

    if (!trendBullish && !trendBearish)
        return "No Aplica | Info";

    if (trendBullish)
    {
        double high1, high2;
        int highBar1, highBar2;

        bool foundHighs = FindLastTwoD1SwingHighs(
            index,
            out high1,
            out high2,
            out highBar1,
            out highBar2);

        if (!foundHighs)
            return "Sin Datos | Info";

        if (index - highBar2 > AoMaxPivotAgeD1)
            return "Referencia Antigua | Info";

        if (high2 <= high1)
            return "Sin Confirmación | Info";

        double ao1 = GetD1AOMaxAroundPivot(highBar1, 2);
        double ao2 = GetD1AOMaxAroundPivot(highBar2, 2);

        if (ao1 <= 0 || ao2 <= 0)
            return "Sin Confirmación | Info";

        if (!IsAoDifferenceSignificant(ao1, ao2))
            return "Sin Confirmación | Info";

        if (ao2 > ao1)
            return "Convergencia Alcista | Info";

        if (ao2 < ao1)
            return "Divergencia Bajista | Info";

        return "Sin Confirmación | Info";
    }

    if (trendBearish)
    {
        double low1, low2;
        int lowBar1, lowBar2;

        bool foundLows = FindLastTwoD1SwingLows(
            index,
            out low1,
            out low2,
            out lowBar1,
            out lowBar2);

        if (!foundLows)
            return "Sin Datos | Info";

        if (index - lowBar2 > AoMaxPivotAgeD1)
            return "Referencia Antigua | Info";

        if (low2 >= low1)
            return "Sin Confirmación | Info";

        double ao1 = GetD1AOMinAroundPivot(lowBar1, 2);
        double ao2 = GetD1AOMinAroundPivot(lowBar2, 2);

        if (ao1 >= 0 || ao2 >= 0)
            return "Sin Confirmación | Info";

        if (!IsAoDifferenceSignificant(ao1, ao2))
            return "Sin Confirmación | Info";

        if (ao2 < ao1)
            return "Convergencia Bajista | Info";

        if (ao2 > ao1)
            return "Divergencia Alcista | Info";

        return "Sin Confirmación | Info";
    }

    return "No Aplica | Info";
}

        private string GetH1Health(
                   int index,
                string h1Trend,
                       SyncState syncState)
{
    if (syncState == SyncState.Off)
    return "No Aplica | Pend";

    if (h1Trend == "ALCISTA")
    {
        double high1, high2;
        int highBar1, highBar2;

        bool foundHighs = FindLastTwoH1SwingHighs(
            index,
            out high1,
            out high2,
            out highBar1,
            out highBar2);

        if (!foundHighs)
            return "Sin Datos | Pend";

         if (index - highBar2 > AoMaxPivotAgeH1)
            return "Referencia Antigua | Pend";

        if (high2 <= high1)
            return "Sin Confirmación | Pend";

        double ao1 = GetH1AOMaxAroundPivot(highBar1, 2);
        double ao2 = GetH1AOMaxAroundPivot(highBar2, 2);

        if (ao1 <= 0 || ao2 <= 0)
            return "Sin Confirmación | Pend";

        if (!IsAoDifferenceSignificant(ao1, ao2))
            return "Sin Confirmación | Pend";


        if (ao2 > ao1)
            return "Convergencia Alcista | Check";

        if (ao2 < ao1)
            return "Divergencia Bajista | Pend";

        return "Sin Confirmación | Pend";
    }

    if (h1Trend == "BAJISTA")
    {
        double low1, low2;
        int lowBar1, lowBar2;

        bool foundLows = FindLastTwoH1SwingLows(
            index,
            out low1,
            out low2,
            out lowBar1,
            out lowBar2);

        if (!foundLows)
            return "Sin Datos | Pend";

        if (index - lowBar2 > AoMaxPivotAgeH1)
            return "Referencia Antigua | Pend";

        if (low2 >= low1)
            return "Sin Confirmación | Pend";

        double ao1 = GetH1AOMinAroundPivot(lowBar1, 2);
        double ao2 = GetH1AOMinAroundPivot(lowBar2, 2);

        if (ao1 >= 0 || ao2 >= 0)
            return "Sin Confirmación | Pend";

        if (!IsAoDifferenceSignificant(ao1, ao2))
            return "Sin Confirmación | Pend";

        if (ao2 < ao1)
            return "Convergencia Bajista | Check";

        if (ao2 > ao1)
            return "Divergencia Alcista | Pend";

        return "Sin Confirmación | Pend";
    }

    return "No Aplica | Pend";
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
         private bool TryGetPreviousH1SwingHigh(int beforeBar, out double price)
{
    price = double.NaN;

    for (int bar = beforeBar - 1; bar >= PivotStrength; bar--)
    {
        if (bar + PivotStrength >= _h1Bars.Count)
            continue;

        if (IsH1SwingHigh(bar, PivotStrength))
        {
            price = _h1Bars.HighPrices[bar];
            return true;
        }
    }

    return false;
}

private bool TryGetPreviousH1SwingLow(int beforeBar, out double price)
{
    price = double.NaN;

    for (int bar = beforeBar - 1; bar >= PivotStrength; bar--)
    {
        if (bar + PivotStrength >= _h1Bars.Count)
            continue;

        if (IsH1SwingLow(bar, PivotStrength))
        {
            price = _h1Bars.LowPrices[bar];
            return true;
        }
    }

    return false;
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
