﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Web.Areas.Admin.Extensions;
using Nop.Web.Areas.Admin.Models.Directory;
using Nop.Web.Framework.Extensions;
using Nop.Web.Framework.Factories;

namespace Nop.Web.Areas.Admin.Factories
{
    /// <summary>
    /// Represents the currency model factory implementation
    /// </summary>
    public partial class CurrencyModelFactory : ICurrencyModelFactory
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly ICurrencyService _currencyService;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly ILocalizedModelFactory _localizedModelFactory;
        private readonly IStoreMappingSupportedModelFactory _storeMappingSupportedModelFactory;
        private readonly IWorkContext _workContext;

        #endregion

        #region Ctor

        public CurrencyModelFactory(CurrencySettings currencySettings,
            ICurrencyService currencyService,
            IDateTimeHelper dateTimeHelper,
            ILocalizedModelFactory localizedModelFactory,
            IStoreMappingSupportedModelFactory storeMappingSupportedModelFactory,
            IWorkContext workContext)
        {
            this._currencySettings = currencySettings;
            this._currencyService = currencyService;
            this._dateTimeHelper = dateTimeHelper;
            this._localizedModelFactory = localizedModelFactory;
            this._storeMappingSupportedModelFactory = storeMappingSupportedModelFactory;
            this._workContext = workContext;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Prepare exchange rate provider model
        /// </summary>
        /// <param name="model">Currency exchange rate provider model</param>
        /// <param name="prepareExchangeRates">Whether to prepare exchange rate models</param>
        protected virtual void PrepareExchangeRateProviderModel(CurrencyExchangeRateProviderModel model, bool liveRates)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            model.AutoUpdateEnabled = _currencySettings.AutoUpdateEnabled;

            //prepare available exchange rate providers
            var availableExchangeRateProviders = _currencyService.LoadAllExchangeRateProviders(_workContext.CurrentCustomer);
            model.ExchangeRateProviders = availableExchangeRateProviders.Select(provider => new SelectListItem
            {
                Text = provider.PluginDescriptor.FriendlyName,
                Value = provider.PluginDescriptor.SystemName,
                Selected = provider.PluginDescriptor.SystemName
                    .Equals(_currencySettings.ActiveExchangeRateProviderSystemName, StringComparison.InvariantCultureIgnoreCase)
            }).ToList();

            //prepare exchange rates
            if (liveRates)
                PrepareExchangeRateModels(model.ExchangeRates);
        }

        /// <summary>
        /// Prepare exchange rate models
        /// </summary>
        /// <param name="models">List of currency exchange rate model</param>
        protected virtual void PrepareExchangeRateModels(IList<CurrencyExchangeRateModel> models)
        {
            if (models == null)
                throw new ArgumentNullException(nameof(models));

            //get primary exchange currency
            var primaryExchangeCurrency = _currencyService.GetCurrencyById(_currencySettings.PrimaryExchangeRateCurrencyId, false)
                ?? throw new NopException("Primary exchange rate currency is not set");

            //get exchange rates
            var exchangeRates = _currencyService.GetCurrencyLiveRates(primaryExchangeCurrency.CurrencyCode);

            //filter by existing currencies
            var currencies = _currencyService.GetAllCurrencies(true, loadCacheableCopy: false);
            exchangeRates = exchangeRates
                .Where(rate => currencies
                    .Any(currency => currency.CurrencyCode.Equals(rate.CurrencyCode, StringComparison.InvariantCultureIgnoreCase))).ToList();

            //prepare models
            foreach (var rate in exchangeRates)
            {
                models.Add(new CurrencyExchangeRateModel { CurrencyCode = rate.CurrencyCode, Rate = rate.Rate });
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Prepare currency search model
        /// </summary>
        /// <param name="model">Currency search model</param>
        /// <param name="prepareExchangeRates">Whether to prepare exchange rate models</param>
        /// <returns>Currency search model</returns>
        public virtual CurrencySearchModel PrepareCurrencySearchModel(CurrencySearchModel model, bool prepareExchangeRates = false)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            //prepare exchange rate provider model
            PrepareExchangeRateProviderModel(model.ExchangeRateProviderModel, prepareExchangeRates);

            //prepare page parameters
            model.SetGridPageSize();
            model.PageSize = 1000;

            return model;
        }

        /// <summary>
        /// Prepare paged currency list model
        /// </summary>
        /// <param name="searchModel">Currency search model</param>
        /// <returns>Currency list model</returns>
        public virtual CurrencyListModel PrepareCurrencyListModel(CurrencySearchModel searchModel)
        {
            if (searchModel == null)
                throw new ArgumentNullException(nameof(searchModel));

            //get currencies
            var currencies = _currencyService.GetAllCurrencies(showHidden: true, loadCacheableCopy: false);

            //prepare list model
            var model = new CurrencyListModel
            {
                Data = currencies.PaginationByRequestModel(searchModel).Select(currency =>
                {
                    //fill in model values from the entity
                    var currencyModel = currency.ToModel();

                    //fill in additional values (not existing in the entity)
                    currencyModel.IsPrimaryExchangeRateCurrency = currency.Id == _currencySettings.PrimaryExchangeRateCurrencyId;
                    currencyModel.IsPrimaryStoreCurrency = currency.Id == _currencySettings.PrimaryStoreCurrencyId;

                    return currencyModel;
                }),
                Total = currencies.Count
            };

            return model;
        }

        /// <summary>
        /// Prepare currency model
        /// </summary>
        /// <param name="model">Currency model</param>
        /// <param name="currency">Currency</param>
        /// <param name="excludeProperties">Whether to exclude populating of some properties of model</param>
        /// <returns>Currency model</returns>
        public virtual CurrencyModel PrepareCurrencyModel(CurrencyModel model, Currency currency, bool excludeProperties = false)
        {
            Action<CurrencyLocalizedModel, int> localizedModelConfiguration = null;

            if (currency != null)
            {
                //fill in model values from the entity
                model = model ?? currency.ToModel();

                //convert dates to the user time
                model.CreatedOn = _dateTimeHelper.ConvertToUserTime(currency.CreatedOnUtc, DateTimeKind.Utc);

                //define localized model configuration action
                localizedModelConfiguration = (locale, languageId) =>
                {
                    locale.Name = currency.GetLocalized(entity => entity.Name, languageId, false, false);
                };
            }

            //set default values for the new model
            if (currency == null)
            {
                model.Published = true;
                model.Rate = 1;
            }

            //prepare localized models
            if (!excludeProperties)
                model.Locales = _localizedModelFactory.PrepareLocalizedModels(localizedModelConfiguration);

            //prepare available stores
            _storeMappingSupportedModelFactory.PrepareModelStores(model, currency, excludeProperties);

            return model;
        }

        #endregion
    }
}