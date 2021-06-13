﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using OpenQA.Selenium;
using PE_Scrapping.Entidades;
using System.Text.Json;
using System.Threading;

namespace PE_Scrapping.Funciones
{
    public class Funciones
    {
        IWebDriver _driver;
        EndPointSet _endPointSet;
        AppConfig _config;
        public Funciones()
        {

        }
        public Funciones(IWebDriver driver, AppConfig config, string opcion)
        {
            _driver = driver;
            _config = config;
            _endPointSet = opcion.Equals(Constantes.ProcesarPrimeraV) ? _config.Api.First : _config.Api.Second;
        }
        public void GetData()
        {
            var tr = new Transacciones(_config.ConnectionString);
            if (!Directory.Exists(Path.Combine(_config.SavePath, _endPointSet.Title)))
            {
                Directory.CreateDirectory(Path.Combine(_config.SavePath, _endPointSet.Title));
            }
            Console.WriteLine("Limpiando tablas...");
            tr.LimpiarData();

            Console.WriteLine("Obteniendo data de Ubigeo - {0}", _endPointSet.Title);
            var json = SendApiRequest(_endPointSet.BaseUri + _endPointSet.Ubigeo, _endPointSet.BodyTag);

            Ubigeo ubigeos =
                JsonSerializer.Deserialize<Ubigeo>(json);
            tr.GuardarUbigeos(ubigeos);

            ProcessAmbit(ubigeos.ubigeos.nacional);
            ProcessAmbit(ubigeos.ubigeos.extranjero);
        }
        public string SendApiRequest(string url, string tag)
        {
            bool success = false;
            string json = string.Empty;            
            while (!success)
            {
                try
                {
                    _driver.Navigate().GoToUrl(url);
                    Thread.Sleep(_config.MilisecondsWait);
                    json = _driver.FindElement(By.TagName(tag)).Text;
                    success = true;
                }
                catch { }
            }            
            return json;
        }
        public void ProcessAmbit(object ambito)
        {
            List<Department> dep = new();
            List<Province> pro = new();
            List<District> dis = new();
            string ambito_desc = string.Empty;
            string json = string.Empty;
            var fn = new Transacciones(_config.ConnectionString);
            switch (ambito.GetType().Name)
            {
                case Constantes.AmbitoNacional:
                    Nacional nacional = (Nacional)ambito;
                    dep = nacional.departments;
                    pro = nacional.provinces;
                    dis = nacional.districts;
                    ambito_desc = Constantes.AmbitoNacional;
                    break;
                case Constantes.AmbitoExtranjero:
                    Extranjero extranjero = (Extranjero)ambito;
                    dep = extranjero.continents;
                    pro = extranjero.countries;
                    dis = extranjero.states;
                    ambito_desc = Constantes.AmbitoExtranjero;
                    break;
            }

            int index_dep = 0;
            Console.WriteLine("Procesando ámbito: {0}", ambito_desc);
            dep.ForEach(d =>
            {
                index_dep++;
                Console.WriteLine("{0}.- {1}", index_dep.ToString(), d.DESC_DEP);
                int index_pro = 0;
                List<Province> level2 = pro.Where(f => f.CDGO_PADRE.Equals(d.CDGO_DEP)).ToList();
                level2.ForEach(dd =>
                {
                    index_pro++;
                    Console.WriteLine("{0}.{1}.- {2}", index_dep.ToString(), index_pro.ToString(), dd.DESC_PROV);
                    int index_dis = 0;
                    List<District> level3 = dis.Where(f => f.CDGO_PADRE.Equals(dd.CDGO_PROV)).ToList();
                    level3.ForEach(ddd =>
                    {
                        index_dis++;
                        Console.WriteLine("{0}.{1}.{2}.- {3}", index_dep.ToString(), index_pro.ToString(), index_dis.ToString(), ddd.DESC_DIST);
                        int index_loc = 0;
                        json = SendApiRequest(_endPointSet.BaseUri + _endPointSet.Locale.Replace("{ubigeo_code}", ddd.CDGO_DIST), _endPointSet.BodyTag);
                        Local locales = JsonSerializer.Deserialize<Local>(json);
                        fn.GuardarLocales(locales);
                        locales.locales.ForEach(l =>
                        {
                            index_loc++;
                            Console.WriteLine("{0}.{1}.{2}.{3}.- Local: {4}", index_dep.ToString(), index_pro.ToString(), index_dis.ToString(), index_loc.ToString(), l.TNOMB_LOCAL);
                            int index_mes = 0;
                            json = SendApiRequest(_endPointSet.BaseUri + _endPointSet.Table
                                .Replace("{ubigeo_code}", ddd.CDGO_DIST)
                                .Replace("{locale_code}", l.CCODI_LOCAL)
                                , _endPointSet.BodyTag);
                            Mesa mesas = JsonSerializer.Deserialize<Mesa>(json);
                            fn.GuardarMesas(mesas, l.CCODI_LOCAL, l.CCODI_UBIGEO);
                            mesas.mesasVotacion.ForEach(m =>
                            {
                                index_mes++;
                                Console.WriteLine("{0}.{1}.{2}.{3}.{4}.- Mesa N° {5}", index_dep.ToString(), index_pro.ToString(), index_dis.ToString(), index_loc.ToString(), index_mes.ToString(), m.NUMMESA);
                                if (m.PROCESADO == 1)
                                {
                                    json = SendApiRequest(_endPointSet.BaseUri + _endPointSet.TableDetail
                                        .Replace("{table_code}", m.NUMMESA)
                                        , _endPointSet.BodyTag);
                                    MesaDetalle mesaDetalle = JsonSerializer.Deserialize<MesaDetalle>(json);
                                    fn.GuardarMesaDetalle(mesaDetalle, m.NUMMESA);
                                    if (_config.DownloadFiles)
                                        DescargarActa(mesaDetalle.procesos.generalPre.imageActa, 
                                            string.Concat(d.DESC_DEP, "_", dd.DESC_PROV, "_", ddd.DESC_DIST, "_", l.CCODI_LOCAL, "_", m.NUMMESA, ".pdf"));
                                }
                                else Console.WriteLine("---->Mesa no procesada.");
                            });
                        });
                    });
                });
            });
        }
        public void DescargarActa(string url_acta, string save_file)
        {
            using (var client = new WebClient())
            {
                client.DownloadFile(url_acta, Path.Combine(_config.SavePath, _endPointSet.Title, save_file));
            }
        }
    }
}
