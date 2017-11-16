﻿using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Moneris.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.Moneris.Controllers
{
    public class PaymentMonerisController : BasePaymentController
    {
        #region Fields

        private readonly ISettingService _settingService;
        private readonly IPaymentService _paymentService;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly ILocalizationService _localizationService;
        private readonly MonerisPaymentSettings _monerisPaymentSettings;
        private readonly PaymentSettings _paymentSettings;
        private readonly IWebHelper _webHelper;
        private readonly IPermissionService _permissionService;

        #endregion

        #region Ctor

        public PaymentMonerisController(ISettingService settingService,
            IPaymentService paymentService,
            IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            ILocalizationService localizationService,
            MonerisPaymentSettings monerisPaymentSettings,
            PaymentSettings paymentSettings,
            IWebHelper webHelper,
            IPermissionService permissionService)
        {
            this._settingService = settingService;
            this._paymentService = paymentService;
            this._orderService = orderService;
            this._orderProcessingService = orderProcessingService;
            this._localizationService = localizationService;
            this._monerisPaymentSettings = monerisPaymentSettings;
            this._paymentSettings = paymentSettings;
            this._webHelper = webHelper;
            this._permissionService = permissionService;
        }

        #endregion

        #region Utilites

        private string GetValue(string key, IFormCollection form)
        {
            return (form.Keys.Contains(key) ? form[key].ToString() : _webHelper.QueryString<string>(key)) ?? string.Empty;
        }

        #endregion

        #region Methods

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var model = new ConfigurationModel
            {
                AdditionalFee = _monerisPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = _monerisPaymentSettings.AdditionalFeePercentage,
                HppKey = _monerisPaymentSettings.HppKey,
                PsStoreId = _monerisPaymentSettings.PsStoreId,
                UseSandbox = _monerisPaymentSettings.UseSandbox
            };

            return View("~/Plugins/Payments.Moneris/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return Configure();

            //save settings
            _monerisPaymentSettings.AdditionalFee = model.AdditionalFee;
            _monerisPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;
            _monerisPaymentSettings.HppKey = model.HppKey;
            _monerisPaymentSettings.PsStoreId = model.PsStoreId;
            _monerisPaymentSettings.UseSandbox = model.UseSandbox;
            _settingService.SaveSetting(_monerisPaymentSettings);

            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }

        public IActionResult SuccessCallbackHandler()
        {
            var processor = _paymentService.LoadPaymentMethodBySystemName("Payments.Moneris") as MonerisPaymentProcessor;
            if (processor == null || !processor.IsPaymentMethodActive(_paymentSettings) || !processor.PluginDescriptor.Installed)
            {
                throw new NopException("Moneris module cannot be loaded");
            }

            var parameters = Request.Form;

            if (string.IsNullOrEmpty(GetValue("transactionKey", parameters)) || string.IsNullOrEmpty(GetValue("rvar_order_id", parameters)))
                return RedirectToAction("Index", "Home", new {area = ""});

            var transactionKey = GetValue("transactionKey", parameters);
            if (!processor.TransactionVerification(transactionKey, out Dictionary<string, string> values))
                return RedirectToAction("Index", "Home", new { area = "" });

            var orderIdValue = GetValue("rvar_order_id", parameters);
            if (!int.TryParse(orderIdValue, out int orderId))
                return RedirectToAction("Index", "Home", new {area = ""});

            var order = _orderService.GetOrderById(orderId);
            if (order == null || !_orderProcessingService.CanMarkOrderAsPaid(order))
                return RedirectToAction("Index", "Home", new {area = ""});

            if (values.ContainsKey("txn_num"))
            {
                order.AuthorizationTransactionId = values["txn_num"];
                _orderService.UpdateOrder(order);
            }

            _orderProcessingService.MarkOrderAsPaid(order);
            return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
        }

        public IActionResult FailCallbackHandler()
        {
            return RedirectToAction("Index", "Home", new { area = "" });
        }

        #endregion
    }
}