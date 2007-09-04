/* Yet Another Forum.NET
 * Copyright (C) 2003-2005 Bj�rnar Henden
 * Copyright (C) 2006-2007 Jaben Cargman
 * http://www.yetanotherforum.net/
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 */

using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Data;
using System.Web.UI.HtmlControls;
using System.Xml;
using System.Web;
using System.Web.Security;
using System.Threading;
using System.Globalization;
using YAF.Classes.Data;
using YAF.Classes.Utils;

namespace YAF.Classes.Base
{
	/// <summary>
	/// Summary description for BasePage.
	/// </summary>
	public class ForumPage : System.Web.UI.UserControl
	{
		/* Ederon : 6/16/2007 - conventions */

		#region Variables
		
		private bool _noDataBase = false;
		private bool _showToolBar = true;
		private bool _checkSuspended = true;
		private string _transPage = string.Empty;

		private YAF.Controls.Header _header = null;
		private YAF.Controls.Footer _footer = null;

		public YAF.Controls.Header ForumHeader
		{
			get
			{
				return _header;
			}
			set
			{
				_header = value;
			}
		}
		public YAF.Controls.Footer ForumFooter
		{
			get
			{
				return _footer;
			}
			set
			{
				_footer = value;
			}
		}

		#endregion

		#region Constructor and events
		/// <summary>
		/// Constructor
		/// </summary>
		public ForumPage() : this( "" ) {}
		public ForumPage( string transPage )
		{
			_transPage = transPage;		

			this.Load += new System.EventHandler( this.ForumPage_Load );
			this.Unload += new System.EventHandler( this.ForumPage_Unload );
			this.Error += new System.EventHandler( this.ForumPage_Error );
			this.PreRender += new EventHandler( ForumPage_PreRender );
		}

		private void ForumPage_Error( object sender, System.EventArgs e )
		{
			// This doesn't seem to work...
			Exception x = Server.GetLastError();
			YAF.Classes.Data.DB.eventlog_create( PageContext.PageUserID, this, x );
			if ( !YafForumInfo.IsLocal )
				General.LogToMail( Server.GetLastError() );
		}

		static public int ValidInt( object expression )
		{
			try
			{
				if (expression == null)
					return 0;

				return int.Parse(expression.ToString());
			}
			catch ( Exception )
			{
				return 0;
			}
		}

		/// <summary>
		/// Called when page is loaded
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void ForumPage_Load( object sender, System.EventArgs e )
		{
			if ( _noDataBase )
				return;

#if DEBUG
			QueryCounter.Reset();
#endif

			if ( ForumFooter != null ) ForumFooter.StopWatch.Start();

			// setup the culture based on the browser...
			InitCulture();

			// checks the DB exists and the version is current, otherwise redirects to in the install page...
			InitDB();

			// deal with banned users...
			CheckBannedIPs();

			// initialize the user and current page data...
			InitUserAndPage();

			// initialize theme
			InitTheme();

			// initialize localization
			InitLocalization();

			//if (user!=null && m_pageinfo["ProviderUserKey"] == DBNull.Value)
			//    throw new ApplicationException("User not migrated to ASP.NET 2.0");

			if ( _checkSuspended && PageContext.IsSuspended )
			{
				if ( PageContext.SuspendedUntil < DateTime.Now )
				{
					YAF.Classes.Data.DB.user_suspend( PageContext.PageUserID, null );
					HttpContext.Current.Response.Redirect( General.GetSafeRawUrl() );
				}
				YafBuildLink.Redirect( ForumPages.info, "i=2" );
			}

			// This happens when user logs in
			if ( Mession.LastVisit == DateTime.MinValue )
			{
				if ( PageContext.UnreadPrivate > 0 )
					PageContext.AddLoadMessage( String.Format( GetText( "UNREAD_MSG" ), PageContext.UnreadPrivate ) );
			}

			if ( !PageContext.IsGuest && PageContext.Page["PreviousVisit"] != DBNull.Value && !Mession.HasLastVisit )
			{
				Mession.LastVisit = Convert.ToDateTime(PageContext.Page["PreviousVisit"]);
				Mession.HasLastVisit = true;
			}
			else if ( Mession.LastVisit == DateTime.MinValue )
			{
				Mession.LastVisit = DateTime.Now;
			}

			// Check if pending mails, and send 10 of them if possible
			if ( Convert.ToInt32(PageContext.Page["MailsPending"]) > 0 )
			{
				try
				{
					using ( DataTable dt = YAF.Classes.Data.DB.mail_list() )
					{
						for ( int i = 0; i < dt.Rows.Count; i++ )
						{
							// Build a MailMessage
							if ( dt.Rows [i] ["ToUser"].ToString().Trim() != String.Empty )
							{
								General.SendMail( PageContext.BoardSettings.ForumEmail, ( string ) dt.Rows [i] ["ToUser"], ( string ) dt.Rows [i] ["Subject"], ( string ) dt.Rows [i] ["Body"] );
							}
							YAF.Classes.Data.DB.mail_delete( dt.Rows [i] ["MailID"] );
						}
						if ( PageContext.IsAdmin ) PageContext.AddAdminMessage( "Sent Mail", String.Format( "Sent {0} mails.", dt.Rows.Count ) );
					}
				}
				catch ( Exception x )
				{
					YAF.Classes.Data.DB.eventlog_create( PageContext.PageUserID, this, x );
					if ( PageContext.IsAdmin )
					{
						PageContext.AddAdminMessage( "Error sending emails to users", x.ToString() );
					}
				}
			}
		}

		/// <summary>
		/// Called when the page is unloaded
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void ForumPage_Unload( object sender, System.EventArgs e )
		{
			// reset stuff...
			PageContext.ResetLoadStrings();
			if ( ForumHeader != null ) ForumHeader.Reset();
			if ( ForumFooter != null ) ForumFooter.Reset();
		}

		/// <summary>
		/// Checks for DB connectivity and DB version
		/// </summary>
		private void InitDB()
		{
      // 1st test if for DB connectivity...
      try
      {
        using ( YAF.Classes.Data.YafDBConnManager connMan = new YafDBConnManager() )
        {
          // just attempt to open the connection to test if a DB is available.
          System.Data.SqlClient.SqlConnection getConn = connMan.OpenDBConnection;
        }
      }
      catch ( System.Data.SqlClient.SqlException ex )
      {
#if !DEBUG
        // unable to connect to the DB...
        Session ["StartupException"] = "Unable to connect to the Database. Exception Message: " + ex.Message + " (" + ex.Number.ToString() + ")";
        Response.Redirect( YafForumInfo.ForumRoot + "error.aspx" );
#else
        // re-throw since we are debugging...
        throw;
#endif
      }

      // step 2: validate the database version...
      try
      {        
        DataTable registry = YAF.Classes.Data.DB.registry_list( "Version" );

        if ( ( registry.Rows.Count == 0 ) || ( Convert.ToInt32( registry.Rows [0] ["Value"] ) < YafForumInfo.AppVersion ) )
        {
          // needs upgrading...
          Response.Redirect( YafForumInfo.ForumRoot + "install/default.aspx?upgrade=1" );
        }
      }
      catch ( System.Data.SqlClient.SqlException )
      {
        // needs to be setup...
        Response.Redirect( YafForumInfo.ForumRoot + "install/" );
      }
		}

		/// <summary>
		/// Look for banned IPs and handle
		/// </summary>
		private void CheckBannedIPs()
		{
			string key = YafCache.GetBoardCacheKey(Constants.Cache.BannedIP);

			// load the banned IP table...
			DataTable bannedIPs = (DataTable)YafCache.Current[key];

			if ( bannedIPs == null )
			{
				// load the table and cache it...
				bannedIPs = DB.bannedip_list( PageContext.PageBoardID, null );
				YafCache.Current[key] = bannedIPs;
			}

			// check for this user in the list...
			foreach ( DataRow row in bannedIPs.Rows )
			{
				if ( General.IsBanned( ( string ) row["Mask"], HttpContext.Current.Request.ServerVariables ["REMOTE_ADDR"] ) )
					HttpContext.Current.Response.End();
			}
		}

		/// <summary>
		/// Set the culture and UI culture to the browser's accept language
		/// </summary>
		private void InitCulture()
		{			
			try
			{
				string cultureCode = "";
				string [] tmp = HttpContext.Current.Request.UserLanguages;
				if ( tmp != null )
				{
					cultureCode = tmp [0];
					if ( cultureCode.IndexOf( ';' ) >= 0 )
					{
						cultureCode = cultureCode.Substring( 0, cultureCode.IndexOf( ';' ) ).Replace( '_', '-' );
					}
				}
				else
				{
					cultureCode = "en-US";
				}

				Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture( cultureCode );
				Thread.CurrentThread.CurrentUICulture = new CultureInfo( cultureCode );

			}
#if DEBUG
			catch ( Exception ex )
			{
				YAF.Classes.Data.DB.eventlog_create( PageContext.PageUserID, this, ex );
				throw new ApplicationException( "Error getting User Language." + Environment.NewLine + ex.ToString() );
			}
#else
			catch(Exception)
			{
				// set to default...
				Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture( "en-US" );
				Thread.CurrentThread.CurrentUICulture = new CultureInfo( "en-US" );
			}
#endif
		}

		/// <summary>
		/// Initialize the user data and page data...
		/// </summary>
		private void InitUserAndPage()
		{
			System.Data.DataRow pageRow;

			// Find user name
			MembershipUser user = Membership.GetUser();
			if ( user != null && Session ["UserUpdated"] == null )
			{
        RoleMembershipHelper.UpdateForumUser( user, PageContext.PageBoardID );
				Session ["UserUpdated"] = true;
			}

			string browser = String.Format( "{0} {1}", HttpContext.Current.Request.Browser.Browser, HttpContext.Current.Request.Browser.Version );
			string platform = HttpContext.Current.Request.Browser.Platform;

			if ( HttpContext.Current.Request.UserAgent != null )
			{
        if ( HttpContext.Current.Request.UserAgent.IndexOf( "Windows NT 5.2" ) >= 0 )
        {
          platform = "Win2003";
        }
        else if ( HttpContext.Current.Request.UserAgent.IndexOf( "Windows NT 6.0" ) >= 0 )
        {
          platform = "Vista";
        }
			}

			int? categoryID = ValidInt( HttpContext.Current.Request.QueryString ["c"] );
			int? forumID = ValidInt( HttpContext.Current.Request.QueryString ["f"] );
			int? topicID = ValidInt( HttpContext.Current.Request.QueryString ["t"] );
			int? messageID = ValidInt( HttpContext.Current.Request.QueryString ["m"] );

			if ( PageContext.Settings.CategoryID != 0 )
				categoryID = PageContext.Settings.CategoryID;

			object userKey = DBNull.Value;

      if (user != null)
      {
        userKey = user.ProviderUserKey;
      }

			do
			{
				pageRow = DB.pageload(
						HttpContext.Current.Session.SessionID,
						PageContext.PageBoardID,
						userKey,
						HttpContext.Current.Request.UserHostAddress,
						HttpContext.Current.Request.FilePath,
						browser,
						platform,
						categoryID,
						forumID,
						topicID,
						messageID );

				// if the user doesn't exist...
				if ( user != null && pageRow == null )
				{
					// create the user...
          if ( !RoleMembershipHelper.DidCreateForumUser( user, PageContext.PageBoardID ) )
						throw new ApplicationException( "Failed to use new user." );
				}

				// only continue if either the page has been loaded or the user has been found...
			} while ( pageRow == null && user != null );

			// page still hasn't been loaded...
			if ( pageRow == null )
			{
				if ( user != null )
					throw new ApplicationException( string.Format( "User '{0}' isn't registered.", user.UserName ) );
				else
					throw new ApplicationException( "Failed to find guest user." );
			}

			// save this page data to the context...
			PageContext.Page = pageRow;
		}

		/// <summary>
		/// Sets the theme class up for usage
		/// </summary>
		private void InitTheme()
		{
			string themeFile = null;

			if ( PageContext.Page != null && PageContext.Page["ThemeFile"] != DBNull.Value && PageContext.BoardSettings.AllowUserTheme )
			{
				// use user-selected theme
				themeFile = PageContext.Page ["ThemeFile"].ToString();
			}
			else if ( PageContext.Page != null && PageContext.Page["ForumTheme"] != DBNull.Value )
			{
				themeFile = PageContext.Page ["ForumTheme"].ToString();
			}
			else
			{
				themeFile = PageContext.BoardSettings.Theme;
			}

			if ( themeFile == null )
			{
				themeFile = "standard.xml";
			}		

			// create the theme class
			PageContext.Theme = new YAF.Classes.Utils.YafTheme( themeFile );
		}

		/// <summary>
		/// Sets up the localization class for usage
		/// </summary>
		private void InitLocalization()
		{
			PageContext.Localization = new YAF.Classes.Utils.YafLocalization(_transPage);
		}
		#endregion

		#region Render Functions

		private void ForumPage_PreRender( object sender, EventArgs e )
		{
			System.Web.UI.HtmlControls.HtmlImage graphctl;
			if ( PageContext.BoardSettings.AllowThemedLogo & !YAF.Classes.Config.IsDotNetNuke & !YAF.Classes.Config.IsPortal & !YAF.Classes.Config.IsRainbow )
			{
				graphctl = ( System.Web.UI.HtmlControls.HtmlImage ) Page.FindControl( "imgBanner" );
				if ( graphctl != null )
				{
					graphctl.Src = GetThemeContents( "FORUM", "BANNER" );
				}
			}

			System.Web.UI.HtmlControls.HtmlTitle ctl;
			ctl = ( System.Web.UI.HtmlControls.HtmlTitle ) Page.FindControl( "ForumTitle" );

			if ( ctl != null )
			{
				System.Text.StringBuilder title = new StringBuilder();
				if ( PageContext.PageTopicID != 0 )
					title.AppendFormat( "{0} - ", General.BadWordReplace( PageContext.PageTopicName ) ); // Tack on the topic we're viewing
				if ( PageContext.PageForumName != string.Empty )
					title.AppendFormat( "{0} - ", Server.HtmlEncode( PageContext.PageForumName ) ); // Tack on the forum we're viewing
				title.Append( Server.HtmlEncode( PageContext.BoardSettings.Name ) ); // and lastly, tack on the board's name
				ctl.Text = title.ToString();
			}

			// setup the forum control header properties
			ForumHeader.SimpleRender = !_showToolBar;
			ForumFooter.SimpleRender = !_showToolBar;	
		}

		/// <summary>
		/// Writes the document
		/// </summary>
		/// <param name="writer"></param>
		protected override void Render( System.Web.UI.HtmlTextWriter writer )
		{
			RenderBody( writer );
		}

		/// <summary>
		/// Renders the body
		/// </summary>
		/// <param name="writer"></param>
		protected virtual void RenderBody( System.Web.UI.HtmlTextWriter writer )
		{
			RenderBase( writer );
		}

		/// <summary>
		/// Calls the base class to render components
		/// </summary>
		/// <param name="writer"></param>
		protected void RenderBase( System.Web.UI.HtmlTextWriter writer )
		{
			base.Render( writer );
		}

		#endregion

		#region Page/User properties
		/// <summary>
		/// Set to true if this is the start page. Should only be set by the page that initialized the database.
		/// </summary>
		protected bool NoDataBase
		{
			set
			{
				_noDataBase = value;
			}
		}
		#endregion

		#region Other
		/// <summary>
		/// Adds a message that is displayed to the user when the page is loaded.
		/// </summary>
		/// <param name="msg">The message to display</param>
		public string RefreshURL
		{
			set
			{
				if ( ForumHeader != null ) ForumHeader.RefreshURL = value;
			}
			get
			{
				if ( ForumHeader != null ) return ForumHeader.RefreshURL;
				return null;
			}
		}
		public int RefreshTime
		{
			set
			{
				if ( ForumHeader != null ) ForumHeader.RefreshTime = value;
			}
			get
			{
				if ( ForumHeader != null ) return ForumHeader.RefreshTime;
				return 0;
			}
		}

		#endregion

		#region Layout functions
		/// <summary>
		/// Set to false if you don't want the menus at top and bottom. Only admin pages will set this to false
		/// </summary>
		protected bool ShowToolBar
		{
			set
			{
				_showToolBar = value;
			}
		}
		#endregion

		public bool CheckSuspended
		{
			set
			{
				_checkSuspended = value;
			}
		}

		static public object IsNull( string value )
		{
			if ( value == null || value.ToLower() == string.Empty )
				return DBNull.Value;
			else
				return value;
		}

		#region PageInfo class

		[Obsolete( "Useless property that always returns true. Do not use anymore." )]
		public bool CanLogin
		{
			get
			{
				return true;
			}
		}

		public MembershipUser User
		{
			get
			{
				return PageContext.User;
			}
		}

		public string LoadMessage
		{
			get
			{
				return PageContext.LoadString;
			}
		}

		#region Theme Helper Functions
		/// <summary>
		/// Get a value from the currently configured forum theme.
		/// </summary>
		/// <param name="page">Page to look under</param>
		/// <param name="tag">Theme item</param>
		/// <returns>Converted Theme information</returns>
		public string GetThemeContents( string page, string tag )
		{
			return PageContext.Theme.GetItem( page, tag );
		}

		/// <summary>
		/// Get a value from the currently configured forum theme.
		/// </summary>
		/// <param name="page">Page to look under</param>
		/// <param name="tag">Theme item</param>
		/// <param name="defaultValue">Value to return if the theme item doesn't exist</param>
		/// <param name="dontLogMissing">True if you don't want a log created if it doesn't exist</param>
		/// <returns>Converted Theme information or Default Value if it doesn't exist</returns>
		public string GetThemeContents( string page, string tag, string defaultValue )
		{
			return PageContext.Theme.GetItem( page, tag, defaultValue );
		}

		/// <summary>
		/// Gets the collapsible panel image url (expanded or collapsed). 
		/// 
		/// <param name="panelID">ID of collapsible panel</param>
		/// <param name="defaultState">Default Panel State</param>
		/// </summary>
		/// <returns>Image URL</returns>
		public string GetCollapsiblePanelImageURL( string panelID, PanelSessionState.CollapsiblePanelState defaultState )
		{
			PanelSessionState.CollapsiblePanelState stateValue = Mession.PanelState [panelID];
			if ( stateValue == PanelSessionState.CollapsiblePanelState.None )
			{
				stateValue = defaultState;
				Mession.PanelState [panelID] = defaultState;
			}

			return GetThemeContents( "ICONS", ( stateValue == PanelSessionState.CollapsiblePanelState.Expanded ? "PANEL_COLLAPSE" : "PANEL_EXPAND" ) );
		}
		#endregion		

		#region Localization Helper Functions

		public string GetText( string text )
		{
			return PageContext.Localization.GetText( text );
		}

		public string GetText( string page, string text )
		{
			return PageContext.Localization.GetText( page, text );
		}

		#endregion

		/// <summary>
		/// Gets the current forum Context (helper reference)
		/// </summary>
		public YAF.Classes.Utils.YafContext PageContext
		{
			get
			{
				return YAF.Classes.Utils.YafContext.Current;
			}
		}

		public string ForumURL
		{
			get
			{
				return string.Format( "{0}{1}", YafForumInfo.ServerURL, YafBuildLink.GetLink( ForumPages.forum ) );
			}
		}

		#endregion

		protected string HtmlEncode( object data )
		{
			return Server.HtmlEncode( data.ToString() );
		}

        /// <summary>
        /// Adds the given CSS to the page header within a <![CDATA[<style>]]> tag
        /// </summary>
        /// <param name="cssContents">The contents of the text/css style block</param>
        public void RegisterClientCssBlock(string cssContents)
        {
            HtmlHead header = Page.FindControl("HeadTag") as HtmlHead;
            if (header == null)
                return;
	        HtmlGenericControl style = new HtmlGenericControl();
            style.TagName = "style";
            style.Attributes.Add("type", "text/css");
            style.InnerText = cssContents;
	        header.Controls.AddAt(header.Controls.Count, style); // Add to the end of the controls collection
        }
	}
}
