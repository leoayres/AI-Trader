// Robô: Rompimento por Variação do Corpo do Candle  (port fiel do NTSL / ProfitPro)
// Ativo: WIN | Gráfico: 2 minutos | NinjaTrader 8
// Instalação: Nova > Editor NinjaScript > Strategies > New Strategy (ou importe este .cs em
// Documents\NinjaTrader 8\bin\Custom\Strategies\) e compile (F5).
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class RoboRompimentoVariacaoCandle : Strategy
	{
		// --- gestão da operação aberta ---
		private double precoStop;
		private double precoAlvo;
		private bool   configurouRisco;

		// --- gerenciamento diário ---
		private DateTime dataAtual = DateTime.MinValue;
		private int  maxOperacoesDia, maxGainsDia, maxLossDia;
		private int  operacoesNoDia, gainsNoDia, lossNoDia;
		private bool contabilizouEntrada;
		private bool bloqueadoNoDia;

		// --- apuração do resultado da operação ---
		private bool   tinhaPosicao;
		private double entryPrice;
		private int    entryDir;           // +1 compra, -1 venda
		private double ultimoResultadoPts;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description                     = "Rompimento por variação do corpo do candle (port do NTSL).";
				Name                            = "RoboRompimentoVariacaoCandle";
				Calculate                       = Calculate.OnBarClose;   // lógica avaliada no fechamento do candle
				EntriesPerDirection             = 1;
				EntryHandling                   = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy    = false;                  // encerramento é feito pelo horário do robô
				ExitOnSessionCloseSeconds       = 30;
				IsFillLimitOnTouch              = false;
				MaximumBarsLookBack             = MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution             = OrderFillResolution.Standard;
				Slippage                        = 0;
				StartBehavior                   = StartBehavior.WaitUntilFlat;
				TimeInForce                     = TimeInForce.Gtc;
				TraceOrders                     = false;
				RealtimeErrorHandling           = RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling              = StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade             = 1;

				VariacaoMinimaPontos            = 250;
				StopTecnicoMaximoPontos         = 500;
				AlvoPontos                      = 250;
				QtdeContratos                   = 1;
				PerfilGerenciamento             = 1;
				OffsetProtecaoStopPontos        = 20;
				EncerraNoFimDoDia               = true;
				HorarioEncerramento             = 1745;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToTrade)
				return;

			// 1) Reinício dos contadores a cada novo dia
			if (Time[0].Date != dataAtual)
			{
				dataAtual           = Time[0].Date;
				operacoesNoDia      = 0;
				gainsNoDia          = 0;
				lossNoDia           = 0;
				bloqueadoNoDia      = false;
				contabilizouEntrada = false;
			}

			// 2) Perfil de gerenciamento diário
			if (PerfilGerenciamento == 2) { maxOperacoesDia = 3; maxGainsDia = 2; maxLossDia = 2; }
			else                          { maxOperacoesDia = 5; maxGainsDia = 3; maxLossDia = 3; }

			if (gainsNoDia >= maxGainsDia || lossNoDia >= maxLossDia || operacoesNoDia >= maxOperacoesDia)
				bloqueadoNoDia = true;

			bool comPosicao = Position.MarketPosition != MarketPosition.Flat;

			// 3) Gatilho (fechamento do candle) + filtro de risco
			double corpo = Math.Abs(Close[0] - Open[0]);
			bool disparaCompra = false, disparaVenda = false;
			double stopGatilho = 0;

			if (!comPosicao && !bloqueadoNoDia && corpo >= VariacaoMinimaPontos)
			{
				if (Close[0] > Open[0])
				{
					if (Close[0] - Low[0] <= StopTecnicoMaximoPontos) { disparaCompra = true; stopGatilho = Low[0]; }
				}
				else if (Close[0] < Open[0])
				{
					if (High[0] - Close[0] <= StopTecnicoMaximoPontos) { disparaVenda = true; stopGatilho = High[0]; }
				}
			}

			// 4) Ordem a mercado (preenche na abertura do candle seguinte)
			if (disparaCompra)
			{
				EnterLong(QtdeContratos, "Compra");
				precoStop       = stopGatilho;
				configurouRisco = false;
			}
			if (disparaVenda)
			{
				EnterShort(QtdeContratos, "Venda");
				precoStop       = stopGatilho;
				configurouRisco = false;
			}

			// 5) Gestão da posição aberta: alvo (a partir do preço médio) e stop-limit no candle gatilho
			if (Position.MarketPosition == MarketPosition.Long)
			{
				if (!configurouRisco)
				{
					precoAlvo       = Position.AveragePrice + AlvoPontos;
					configurouRisco = true;
				}
				double limiteStop = precoStop - OffsetProtecaoStopPontos;
				ExitLongStopLimit(Position.Quantity, limiteStop, precoStop, "StopC", "Compra");
				ExitLongLimit(Position.Quantity, precoAlvo, "AlvoC", "Compra");

				if (Close[0] < limiteStop)           // proteção contra gap
					ExitLong(Position.Quantity, "GapC", "Compra");
			}
			else if (Position.MarketPosition == MarketPosition.Short)
			{
				if (!configurouRisco)
				{
					precoAlvo       = Position.AveragePrice - AlvoPontos;
					configurouRisco = true;
				}
				double limiteStop = precoStop + OffsetProtecaoStopPontos;
				ExitShortStopLimit(Position.Quantity, limiteStop, precoStop, "StopV", "Venda");
				ExitShortLimit(Position.Quantity, precoAlvo, "AlvoV", "Venda");

				if (Close[0] > limiteStop)
					ExitShort(Position.Quantity, "GapV", "Venda");
			}

			// 6) Encerramento forçado no fim do dia
			if (EncerraNoFimDoDia && ToTime(Time[0]) >= HorarioEncerramento * 100 && comPosicao)
			{
				if (Position.MarketPosition == MarketPosition.Long)  ExitLong(Position.Quantity, "FimDiaC", "Compra");
				if (Position.MarketPosition == MarketPosition.Short) ExitShort(Position.Quantity, "FimDiaV", "Venda");
			}

			// 7) Contabiliza a operação (uma vez por entrada)
			if (comPosicao && !contabilizouEntrada)
			{
				operacoesNoDia++;
				contabilizouEntrada = true;
			}

			// 8) Resultado ao encerrar a posição (gain/loss cumulativo no dia)
			if (!comPosicao && tinhaPosicao)
			{
				if (ultimoResultadoPts > 0)      gainsNoDia++;
				else if (ultimoResultadoPts < 0) lossNoDia++;

				configurouRisco     = false;
				contabilizouEntrada = false;
			}
			tinhaPosicao = comPosicao;
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
			MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution.Order == null || execution.Order.OrderState != OrderState.Filled)
				return;

			OrderAction a = execution.Order.OrderAction;
			if (a == OrderAction.Buy || a == OrderAction.SellShort)
			{
				entryPrice = price;
				entryDir   = a == OrderAction.Buy ? 1 : -1;
			}
			else if (marketPosition == MarketPosition.Flat)
			{
				ultimoResultadoPts = (price - entryPrice) * entryDir;
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "Variação mínima (pontos)", Description = "Corpo mínimo do candle gatilho", Order = 1, GroupName = "Parâmetros")]
		public double VariacaoMinimaPontos { get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "Stop técnico máximo (pontos)", Description = "Distância máxima até a extremidade oposta do candle gatilho", Order = 2, GroupName = "Parâmetros")]
		public double StopTecnicoMaximoPontos { get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "Alvo (pontos)", Order = 3, GroupName = "Parâmetros")]
		public double AlvoPontos { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Contratos", Order = 4, GroupName = "Parâmetros")]
		public int QtdeContratos { get; set; }

		[NinjaScriptProperty]
		[Range(1, 2)]
		[Display(Name = "Perfil de gerenciamento (1 ou 2)", Description = "1 = até 5 ops, para em 3 gains/3 losses; 2 = até 3 ops, para em 2 gains/2 losses", Order = 5, GroupName = "Parâmetros")]
		public int PerfilGerenciamento { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Offset proteção stop (pontos)", Order = 6, GroupName = "Parâmetros")]
		public double OffsetProtecaoStopPontos { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Encerra no fim do dia", Order = 7, GroupName = "Parâmetros")]
		public bool EncerraNoFimDoDia { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "Horário encerramento (HHMM)", Order = 8, GroupName = "Parâmetros")]
		public int HorarioEncerramento { get; set; }
		#endregion
	}
}
