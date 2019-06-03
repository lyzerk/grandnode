﻿using Grand.Core;
using Grand.Core.Domain.Customers;
using Grand.Framework.Controllers;
using Grand.Framework.Kendoui;
using Grand.Framework.Mvc;
using Grand.Framework.Mvc.Filters;
using Grand.Framework.Security.Authorization;
using Grand.Services.Catalog;
using Grand.Services.Customers;
using Grand.Services.ExportImport;
using Grand.Services.Localization;
using Grand.Services.Security;
using Grand.Services.Seo;
using Grand.Services.Stores;
using Grand.Web.Areas.Admin.Extensions;
using Grand.Web.Areas.Admin.Interfaces;
using Grand.Web.Areas.Admin.Models.Catalog;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Grand.Web.Areas.Admin.Controllers
{
    [PermissionAuthorize(PermissionSystemName.Categories)]
    public partial class CategoryController : BaseAdminController
    {
        #region Fields

        private readonly ICategoryService _categoryService;
        private readonly ICategoryViewModelService _categoryViewModelService;
        private readonly ICustomerService _customerService;
        private readonly ILanguageService _languageService;
        private readonly ILocalizationService _localizationService;
        private readonly IStoreService _storeService;
        private readonly IExportManager _exportManager;
        private readonly IWorkContext _workContext;
        private readonly IImportManager _importManager;

        #endregion

        #region Constructors

        public CategoryController(
            ICategoryService categoryService,
            ICategoryViewModelService categoryViewModelService,
            ICustomerService customerService,
            ILanguageService languageService,
            ILocalizationService localizationService,
            IStoreService storeService,
            IExportManager exportManager,
            IWorkContext workContext,
            IImportManager importManager)
        {
            this._categoryService = categoryService;
            this._categoryViewModelService = categoryViewModelService;
            this._customerService = customerService;
            this._languageService = languageService;
            this._localizationService = localizationService;
            this._storeService = storeService;
            this._exportManager = exportManager;
            this._workContext = workContext;
            this._importManager = importManager;
        }

        #endregion

        #region List / tree

        public IActionResult Index() => RedirectToAction("List");

        public async Task<IActionResult> List()
        {
            var model = await _categoryViewModelService.PrepareCategoryListModel(_workContext.CurrentCustomer.StaffStoreId);
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> List(DataSourceRequest command, CategoryListModel model)
        {
            if (_workContext.CurrentCustomer.IsStaff())
            {
                model.SearchStoreId = _workContext.CurrentCustomer.StaffStoreId;
            }

            var categories = await _categoryViewModelService.PrepareCategoryListModel(model, command.Page, command.PageSize);
            var gridModel = new DataSourceResult {
                Data = categories.categoryListModel,
                Total = categories.totalCount
            };
            return Json(gridModel);
        }

        public IActionResult Tree() => View();

        public async Task<IActionResult> NodeList()
        {
            var model = await _categoryViewModelService.PrepareCategoryNodeListModel(_workContext.CurrentCustomer.StaffStoreId);
            return Json(model);
        }

        #endregion

        #region Create / Edit / Delete

        public async Task<IActionResult> Create()
        {
            var model = await _categoryViewModelService.PrepareCategoryModel(_workContext.CurrentCustomer.StaffStoreId);
            //locales
            await AddLocales(_languageService, model.Locales);

            return View(model);
        }

        [HttpPost, ParameterBasedOnFormName("save-continue", "continueEditing")]
        public async Task<IActionResult> Create(CategoryModel model, bool continueEditing)
        {
            if (ModelState.IsValid)
            {
                if (_workContext.CurrentCustomer.IsStaff())
                {
                    model.LimitedToStores = true;
                    model.SelectedStoreIds = new string[] { _workContext.CurrentCustomer.StaffStoreId };
                }

                var category = await _categoryViewModelService.InsertCategoryModel(model);
                SuccessNotification(_localizationService.GetResource("Admin.Catalog.Categories.Added"));
                return continueEditing ? RedirectToAction("Edit", new { id = category.Id }) : RedirectToAction("List");
            }

            //If we got this far, something failed, redisplay form
            model = await _categoryViewModelService.PrepareCategoryModel(model, null);
            //ACL
            await model.PrepareACLModel(null, true, _customerService);
            //Stores
            await model.PrepareStoresMappingModel(null, _storeService, true, _workContext.CurrentCustomer.StaffStoreId);

            return View(model);
        }

        public async Task<IActionResult> Edit(string id)
        {
            var category = await _categoryService.GetCategoryById(id);
            if (category == null)
                //No category found with the specified id
                return RedirectToAction("List");

            if (_workContext.CurrentCustomer.IsStaff())
            {
                if (!category.LimitedToStores || (category.Stores.Where(x => x != _workContext.CurrentCustomer.StaffStoreId).Any() && category.LimitedToStores))
                    WarningNotification("You can't edit this category, because it can be used in many stores");
            }

            var model = category.ToModel();
            //locales
            await AddLocales(_languageService, model.Locales, (locale, languageId) =>
            {
                locale.Name = category.GetLocalized(x => x.Name, languageId, false, false);
                locale.Description = category.GetLocalized(x => x.Description, languageId, false, false);
                locale.MetaKeywords = category.GetLocalized(x => x.MetaKeywords, languageId, false, false);
                locale.MetaDescription = category.GetLocalized(x => x.MetaDescription, languageId, false, false);
                locale.MetaTitle = category.GetLocalized(x => x.MetaTitle, languageId, false, false);
                locale.SeName = category.GetSeName(languageId, false, false);
            });
            model = await _categoryViewModelService.PrepareCategoryModel(model, category);
            //ACL
            await model.PrepareACLModel(category, false, _customerService);
            //Stores
            await model.PrepareStoresMappingModel(category, _storeService, false, _workContext.CurrentCustomer.StaffStoreId);

            return View(model);
        }

        [HttpPost, ParameterBasedOnFormName("save-continue", "continueEditing")]
        public async Task<IActionResult> Edit(CategoryModel model, bool continueEditing)
        {
            var category = await _categoryService.GetCategoryById(model.Id);
            if (category == null)
                //No category found with the specified id
                return RedirectToAction("List");

            if (_workContext.CurrentCustomer.IsStaff())
            {
                if (!category.LimitedToStores || (category.Stores.Where(x => x != _workContext.CurrentCustomer.StaffStoreId).Any() && category.LimitedToStores))
                    return RedirectToAction("Edit", new { id = category.Id });
            }

            if (ModelState.IsValid)
            {
                if (_workContext.CurrentCustomer.IsStaff())
                {
                    model.LimitedToStores = true;
                    model.SelectedStoreIds = new string[] { _workContext.CurrentCustomer.StaffStoreId };
                }

                category = await _categoryViewModelService.UpdateCategoryModel(category, model);

                SuccessNotification(_localizationService.GetResource("Admin.Catalog.Categories.Updated"));
                if (continueEditing)
                {
                    //selected tab
                    SaveSelectedTabIndex();

                    return RedirectToAction("Edit", new { id = category.Id });
                }
                return RedirectToAction("List");
            }

            //If we got this far, something failed, redisplay form
            model = await _categoryViewModelService.PrepareCategoryModel(model, category);
            //ACL
            await model.PrepareACLModel(category, true, _customerService);
            //Stores
            await model.PrepareStoresMappingModel(category, _storeService, true, _workContext.CurrentCustomer.StaffStoreId);

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var category = await _categoryService.GetCategoryById(id);
            if (category == null)
                //No category found with the specified id
                return RedirectToAction("List");

            if (_workContext.CurrentCustomer.IsStaff())
            {
                if (!category.LimitedToStores || (category.Stores.Where(x => x != _workContext.CurrentCustomer.StaffStoreId).Any() && category.LimitedToStores))
                    return RedirectToAction("Edit", new { id = category.Id });
            }
            if (ModelState.IsValid)
            {
                await _categoryViewModelService.DeleteCategory(category);
                SuccessNotification(_localizationService.GetResource("Admin.Catalog.Categories.Deleted"));
            }
            return RedirectToAction("List");
        }


        #endregion

        #region Export / Import

        public async Task<IActionResult> ExportXml()
        {
            try
            {
                var xml = await _exportManager.ExportCategoriesToXml(await _categoryService.GetAllCategories(showHidden: true, storeId: _workContext.CurrentCustomer.StaffStoreId));
                return File(Encoding.UTF8.GetBytes(xml), "application/xml", "categories.xml");
            }
            catch (Exception exc)
            {
                ErrorNotification(exc);
                return RedirectToAction("List");
            }
        }

        public async Task<IActionResult> ExportXlsx()
        {
            try
            {
                var bytes = _exportManager.ExportCategoriesToXlsx(await _categoryService.GetAllCategories(showHidden: true, storeId: _workContext.CurrentCustomer.StaffStoreId));
                return File(bytes, "text/xls", "categories.xlsx");
            }
            catch (Exception exc)
            {
                ErrorNotification(exc);
                return RedirectToAction("List");
            }
        }

        [HttpPost]
        public async Task<IActionResult> ImportFromXlsx(IFormFile importexcelfile)
        {
            //a vendor and staff cannot import categories
            if (_workContext.CurrentVendor != null || _workContext.CurrentCustomer.IsStaff())
                return AccessDeniedView();

            try
            {
                if (importexcelfile != null && importexcelfile.Length > 0)
                {
                    await _importManager.ImportCategoryFromXlsx(importexcelfile.OpenReadStream());
                }
                else
                {
                    ErrorNotification(_localizationService.GetResource("Admin.Common.UploadFile"));
                    return RedirectToAction("List");
                }
                SuccessNotification(_localizationService.GetResource("Admin.Catalog.Category.Imported"));
                return RedirectToAction("List");
            }
            catch (Exception exc)
            {
                ErrorNotification(exc);
                return RedirectToAction("List");
            }
        }
        #endregion

        #region Products

        [HttpPost]
        public async Task<IActionResult> ProductList(DataSourceRequest command, string categoryId)
        {
            var productCategories = await _categoryViewModelService.PrepareCategoryProductModel(categoryId, command.Page, command.PageSize);
            var gridModel = new DataSourceResult {
                Data = productCategories.categoryProductModels,
                Total = productCategories.totalCount
            };

            return Json(gridModel);
        }

        public async Task<IActionResult> ProductUpdate(CategoryModel.CategoryProductModel model)
        {
            if (ModelState.IsValid)
            {
                await _categoryViewModelService.UpdateProductCategoryModel(model);
                return new NullJsonResult();
            }
            return ErrorForKendoGridJson(ModelState);
        }

        public async Task<IActionResult> ProductDelete(CategoryModel.CategoryProductModel model)
        {
            if (ModelState.IsValid)
            {
                await _categoryViewModelService.DeleteProductCategoryModel(model.Id, model.ProductId);
                return new NullJsonResult();
            }
            return ErrorForKendoGridJson(ModelState);
        }

        public async Task<IActionResult> ProductAddPopup(string categoryId)
        {
            var model = await _categoryViewModelService.PrepareAddCategoryProductModel(_workContext.CurrentCustomer.StaffStoreId);
            model.CategoryId = categoryId;
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> ProductAddPopupList(DataSourceRequest command, CategoryModel.AddCategoryProductModel model)
        {
            var gridModel = new DataSourceResult();
            if (_workContext.CurrentCustomer.IsStaff())
            {
                model.SearchStoreId = _workContext.CurrentCustomer.StaffStoreId;
            }
            var products = await _categoryViewModelService.PrepareProductModel(model, command.Page, command.PageSize);
            gridModel.Data = products.products.ToList();
            gridModel.Total = products.totalCount;

            return Json(gridModel);
        }

        [HttpPost]
        [FormValueRequired("save")]
        public async Task<IActionResult> ProductAddPopup(CategoryModel.AddCategoryProductModel model)
        {
            if (ModelState.IsValid)
            {
                if (model.SelectedProductIds != null)
                {
                    await _categoryViewModelService.InsertCategoryProductModel(model);
                }
                ViewBag.RefreshPage = true;
            }
            else
                ErrorNotification(ModelState);

            return View(model);
        }

        #endregion

        #region Activity log

        [HttpPost]
        public async Task<IActionResult> ListActivityLog(DataSourceRequest command, string categoryId)
        {
            var activityLog = await _categoryViewModelService.PrepareActivityLogModel(categoryId, command.Page, command.PageSize);
            var gridModel = new DataSourceResult {
                Data = activityLog.activityLogModel,
                Total = activityLog.totalCount
            };

            return Json(gridModel);
        }

        #endregion
    }
}
