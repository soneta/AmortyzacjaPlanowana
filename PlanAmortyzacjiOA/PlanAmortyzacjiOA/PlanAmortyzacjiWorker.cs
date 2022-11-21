using System;
using System.Collections.Generic;
using System.Linq;

using Soneta.Business;
using Soneta.Core;
using Soneta.Ksiega;
using Soneta.SrodkiTrwale;
using Soneta.Tools;
using Soneta.Types;

[assembly: Worker(typeof(PlanAmortyzacjiOA.PlanAmortyzacjiWorker), typeof(PKEwidencja))]

namespace PlanAmortyzacjiOA
{

    internal class PlanAmortyzacjiWorker
    {
        internal static class AmortyzacjaQuery
        {
            internal sealed class Params
            {
                internal Date DataOperacji;
                internal TypSrodkaTrwalego TypSrodkaTrwalego = TypSrodkaTrwalego.Brak;
                internal MiejsceUzytkowaniaLookupItem MiejsceUzytkowania;
                internal CentrumKosztowLookupItem CentrumKosztow;
                internal RowCondition Condition;
            }


            internal static List<SrodekTrwalyBaseHistoria> QueryAmortyzacja(Session session, Params prms)
            {
                // wyjatki amortyzacji
                var module = SrodkiTrwaleModule.GetInstance(session);
                var hsExceptions = QueryExceptions(module, prms.DataOperacji);

                // historia srodkow trwalych
                var vStHist = module.SrodkiTrwaleHist.CreateView();
                vStHist.Sort = "ID";
                vStHist.LoadingRowsCount = int.MaxValue;
                vStHist.Condition &= GetConditionAmortyzacja(prms);

                //
                // budowa listy amortyzacyjnej
                //

                var coStHist = new List<SrodekTrwalyBaseHistoria>();
                var unqCheck = new HashSet<SrodekTrwalyBase>();
                var pocztekMiesiacaAmortyzacji = prms.DataOperacji.FirstDayMonth();

                foreach (var sHist in vStHist.Cast<SrodekTrwalyBaseHistoria>())
                {
                    // sprawdzenie duplikatow
                    if (unqCheck.Contains(sHist.Srodek))
                        throw new ApplicationException("AmortyzacjaQuery.QueryAmortyzacja: zdublowany środek trwały: {0}".TranslateFormat(sHist.Srodek));
                    unqCheck.Add(sHist.Srodek);

                    // sprawdzenie czy srodek nadaje sie do amortyzacji 
                    // - dodajemy ST, których data zakończenia amortyzacji jest w miesiącu naliczenia lub późniejsza
                    // - dodajemy ST, które w miesiący naliczania amortyzacji mają wpisane wyjątki amortyzacji
                    if (sHist.Srodek.DataZakonczeniaAmortyzacji >= pocztekMiesiacaAmortyzacji || hsExceptions.Contains(sHist.Srodek))
                    {
                        coStHist.Add(sHist);
                    }
                }

                return coStHist;
            }

            private static HashSet<SrodekTrwalyBase> QueryExceptions(SrodkiTrwaleModule module, Date dataAmortyzacji)
            { return new HashSet<SrodekTrwalyBase>(module.PlanAmortyzacji.WgDaty[dataAmortyzacji].Cast<ElemPlanuAmortyzacji>().Where(e => e.AccessRight != AccessRights.Denied).Select(e => e.Srodek)); }


            private static RowCondition GetConditionAmortyzacja(Params prms)
            {
                var condition = GetCondition(prms);

                if (prms.TypSrodkaTrwalego == TypSrodkaTrwalego.Brak)
                    condition &= new FieldCondition.NotEqual("Typ", TypSrodkaTrwalego.Wyposażenie);

                return condition;
            }


            private static RowCondition GetCondition(Params prms)
            {
                var condition = RowCondition.Empty;

                //-> okres
                condition &= new FieldCondition.LessEqual("Aktualnosc.From", prms.DataOperacji);
                condition &= new FieldCondition.GreaterEqual("Aktualnosc.To", prms.DataOperacji);

                //-> typ srodka trwalego
                if (prms.TypSrodkaTrwalego != TypSrodkaTrwalego.Brak)
                    condition &= new FieldCondition.Equal("Typ", prms.TypSrodkaTrwalego);

                //-> miejsce użytkowania
                if (prms.MiejsceUzytkowania != null && prms.MiejsceUzytkowania != MiejsceUzytkowaniaLookupItem.Wszystkie)
                {
                    if (prms.MiejsceUzytkowania == MiejsceUzytkowaniaLookupItem.Puste)
                        condition &= new FieldCondition.Null("MiejsceUzytkowania", true);
                    else
                        condition &= new FieldCondition.Equal("MiejsceUzytkowania", prms.MiejsceUzytkowania.MiejsceUzytkowania);
                }

                //-> centrum kosztow
                if (prms.CentrumKosztow != null && prms.CentrumKosztow.Nazwa != "[Wszystkie]")
                {
                    if (prms.CentrumKosztow.Nazwa == "[Puste]")
                        condition &= new FieldCondition.Null("CentrumKosztow", true);
                    else
                        condition &= new FieldCondition.Equal("CentrumKosztow", prms.CentrumKosztow.Centrum);
                }

                //-> dodatkowy warunek
                if (prms.Condition != null)
                    condition &= prms.Condition;

                return condition;
            }
        }

        public class Params : ContextBase
        {
            private readonly DokumentZbiorczyGenerujWorker.AmortyzacjaGenerujParametry _amortyzacjaPrm;
            private string _podzielnik1;
            private string _podzielnik2;

            public Params(Context context)
                : base(context)
            {
                _amortyzacjaPrm = new DokumentZbiorczyGenerujWorker.AmortyzacjaGenerujParametry(context);
                Okres = Date.Today.ToYearMonth();
            }

            [Caption("Okres"), Priority(10)]
            public FromTo Okres { get; set; }

            [Caption("Sposób"), Priority(20)]
            public TypGenerowania TypGenerowania
            {
                get => _amortyzacjaPrm.TypGenerowania;
                set => _amortyzacjaPrm.TypGenerowania = value;
            }

            [Caption("Rodziaj ST"), Priority(30)]
            public RodzajST Rodzaj
            {
                get => _amortyzacjaPrm.Rodzaj;
                set => _amortyzacjaPrm.Rodzaj = value;
            }

            [Caption("Rekrusywnie"), Priority(40)]
            public bool Rekursywnie
            {
                get => _amortyzacjaPrm.Rekursywnie;
                set => _amortyzacjaPrm.Rekursywnie = value;
            }

            [Caption("Miejsce"), Priority(50)]
            public MiejsceUzytkowaniaLookupItem MiejsceUzytkowaniaItem
            {
                get => _amortyzacjaPrm.MiejsceUzytkowaniaItem;
                set => _amortyzacjaPrm.MiejsceUzytkowaniaItem = value;
            }

            [Caption("Centrum"), Priority(60)]
            public CentrumKosztowLookupItem CentrumKosztow
            {
                get => _amortyzacjaPrm.CentrumKosztow;
                set => _amortyzacjaPrm.CentrumKosztow = value;
            }

            [Caption("Podzielnik 1"), Priority(70)]
            public string Podzielnik1
            {
                get => _podzielnik1;
                set
                {
                    _podzielnik1 = value;
                    OnChanged();
                }
            }

            [Caption("Podzielnik 2"), Priority(80)]
            public string Podzielnik2
            {
                get => _podzielnik2;
                set
                {
                    _podzielnik2 = value;
                    OnChanged();
                }
            }

            public bool IsReadOnlyRodzaj() => _amortyzacjaPrm.IsReadOnlyRodzaj();

            public bool IsReadOnlyRekursywnie() => _amortyzacjaPrm.IsReadOnlyRekursywnie();


            public LookupInfo GetListRodzaj() => _amortyzacjaPrm.GetListRodzaj();


            public MiejsceUzytkowaniaLookupItem[] GetListMiejsceUzytkowaniaItem() => _amortyzacjaPrm.GetListMiejsceUzytkowaniaItem();

            public CentrumKosztowLookupItem[] GetListCentrumKosztow() => _amortyzacjaPrm.GetListCentrumKosztow();

            public object GetListPodzielnik1()
            {
                RowCondition cond = new FieldCondition.TypeOf(nameof(PodzielnikKosztow.Zrodlo), Session.GetSrodkiTrwale().SrodkiTrwale.TableName);
                if (!string.IsNullOrEmpty(Podzielnik2))
                    cond &= new FieldCondition.NotEqual(nameof(PodzielnikKosztow.Nazwa), Podzielnik2);
                var st = Session.GetCore().PodzielKosztow.WgDefinicja[cond];
                return st.Select(p => p.Nazwa).Distinct().OrderBy(p => p).ToArray();
            }


            public object GetListPodzielnik2()
            {
                RowCondition cond = new FieldCondition.TypeOf(nameof(PodzielnikKosztow.Zrodlo), Session.GetSrodkiTrwale().SrodkiTrwale.TableName);
                if (!string.IsNullOrEmpty(Podzielnik1))
                    cond &= new FieldCondition.NotEqual(nameof(PodzielnikKosztow.Nazwa), Podzielnik1);
                var st = Session.GetCore().PodzielKosztow.WgDefinicja[cond];
                return st.Select(p => p.Nazwa).Distinct().OrderBy(p => p).ToArray();
            }
        }

        [Context]
        public PKEwidencja Ewidencja { get; set; }


        [Action("Generuj opis analityczny dla planowanej amortyzacji", Mode = ActionMode.SingleSession | ActionMode.ConfirmSave, Target = ActionTarget.Menu)]
        public QueryContextInformation GenerujOpisAnalitycznyDlaPlanowanejAmortyzacji()
        {
            return QueryContextInformation.Create<Params>(prms =>
            {
                if (!prms.Okres.IsFullMonth)
                    return "Pole okres musi zawierać pełne miesiące".TranslateIgnore();
                _param = prms;
                _aktualnosc = Ewidencja.DataEwidencji;
                var session = Ewidencja.Session;
                var srodki = from s in AmortyzacjaQuery.QueryAmortyzacja(session, GetQueryParams(_param))
                             where s.Srodek.DataLikwidacji == Date.Empty || s.Srodek.DataLikwidacji >= _param.Okres.To
                             select s.Srodek;

                foreach (var srodek in srodki)
                {
                    var plan = new Plan(srodek, _aktualnosc);
                    var result = GetPodzielniki(plan);

                    using (var transaction = Ewidencja.Session.Logout(true))
                    {
                        foreach (var item in result.Where(p => _param.Okres.Contains(p.Okres)))
                        {
                            var elem = session.AddRow(new ElementOpisuEwidencji(Ewidencja));
                            elem.Wymiar = "Amortyzacja Plan".TranslateIgnore();
                            elem.Symbol = srodek.ToString();
                            elem.Kwota = item.WartoscBilansowa;
                            elem.KwotaDodatkowa = item.WartoscPodatkowa;
                            elem.Opis = item.P1 == null && item.P2 == null
                                ? Ewidencja.Opis
                                : $"{item.P1}; {item.P2}";
                            elem.Data = item.Okres;
                            elem.CentrumKosztow = item.P1 as CentrumKosztow ?? srodek[item.Okres].CentrumKosztow;
                        }

                        transaction.Commit();
                    }
                }


                return null;
            });
        }


        internal AmortyzacjaQuery.Params GetQueryParams(Params paramBase)
        {
            var queryParams = new AmortyzacjaQuery.Params
            {
                DataOperacji = Ewidencja.DataOperacji
            };

            if (paramBase.TypGenerowania == TypGenerowania.SrodkiTrwale)
                queryParams.TypSrodkaTrwalego = TypSrodkaTrwalego.ŚrodekTrwały;

            if (paramBase.TypGenerowania == TypGenerowania.WartosciNiematerialne)
                queryParams.TypSrodkaTrwalego = TypSrodkaTrwalego.WartośćNiematerialnaIPrawna;

            if (paramBase.MiejsceUzytkowaniaItem != MiejsceUzytkowaniaLookupItem.Wszystkie)
                queryParams.MiejsceUzytkowania = paramBase.MiejsceUzytkowaniaItem;

            if (paramBase.CentrumKosztow.Nazwa != "[Wszystkie]")
                queryParams.CentrumKosztow = paramBase.CentrumKosztow;

            if (paramBase.TypGenerowania != TypGenerowania.Wszystkie && paramBase.Rodzaj != null)
            {
                if (paramBase.Rekursywnie)
                    queryParams.Condition = new FieldCondition.Like("KRST.Symbol", paramBase.Rodzaj.Symbol + "*");
                else
                    queryParams.Condition = new FieldCondition.Equal("KRST.Symbol", paramBase.Rodzaj.Symbol);
            }

            return queryParams;
        }



        private Date _aktualnosc;
        Params _param = null;


        private List<ExPlanItem> GetPodzielniki(Plan plan)
        {
            var list = new List<ExPlanItem>();
            int lp = 1;
            foreach (Plan.PlanItem item in plan.Items)
            {

                if (string.IsNullOrEmpty(_param.Podzielnik1) && string.IsNullOrEmpty(_param.Podzielnik2))
                {
                    list.Add(new ExPlanItem { Plan = item, Lp = lp++.ToString() });
                }
                else
                {
                    var wartosci = new[]
                    {
                        item.WartoscBilansowa.Value,
                        item.WartoscPodatkowa.Value
                    };

                    var podzielnik1 = string.IsNullOrEmpty(_param.Podzielnik1) ? null : item.Srodek.Podzielniki[p => p.Nazwa == _param.Podzielnik1].GetFirst();
                    var podzielnik2 = string.IsNullOrEmpty(_param.Podzielnik2) ? null : item.Srodek.Podzielniki[p => p.Nazwa == _param.Podzielnik2].GetFirst();

                    if (podzielnik1 == null && podzielnik2 == null)
                    {
                        throw new RowException(item.Srodek, "Brak podzielnika".Translate());
                    }

                    foreach (var podzielnik in PodzielnikKosztow.Podziel(podzielnik1, podzielnik2, item.Okres, wartosci, 2))
                    {
                        list.Add(new ExPlanItem
                        {
                            Plan = item,
                            P1 = podzielnik.e1?.ElementPodzialowy,
                            P2 = podzielnik.e2?.ElementPodzialowy,
                            WartoscBilansowa = podzielnik.fractions[0],
                            WartoscPodatkowa = podzielnik.fractions[1],
                        });
                    }
                }
            }
            return list;
        }


        public class ExPlanItem
        {

            private decimal? _bilansowa;
            private decimal? _podatkowa;


            public Plan.PlanItem Plan { get; set; }
            public IElementSlownika P1 { get; set; }
            public IElementSlownika P2 { get; set; }
            public string Lp { get; set; }


            public Date Okres
                => Plan != null
                    ? Plan.Okres
                    : Date.Empty;

            public Currency WartoscBilansowa
            {
                get => _bilansowa != null
                        ? new Currency(_bilansowa.Value)
                        : Plan.WartoscBilansowa;
                set => _bilansowa = value.Value;
            }

            public Currency WartoscPodatkowa
            {
                get => _podatkowa != null
                            ? new Currency(_podatkowa.Value)
                            : Plan.WartoscPodatkowa;
                set => _podatkowa = value.Value;
            }
        }


    }
}
