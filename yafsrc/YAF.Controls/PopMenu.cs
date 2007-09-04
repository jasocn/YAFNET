using System;
using System.Data;
using System.Collections;

namespace YAF.Controls
{
	/// <summary>
	/// Summary description for ForumJump.
	/// </summary>
	public class PopMenu : BaseControl, System.Web.UI.IPostBackEventHandler
	{
		private string _control = string.Empty;
		private Hashtable _items = new Hashtable();

		public string Control
		{
			set
			{
				_control = value;
			}
			get
			{
				return _control;
			}
		}

		protected string ControlID
		{
			get
			{
				return string.Format( "{0}_{1}", Parent.ClientID, _control );
			}
		}

		public void AddItem( string title, string script )
		{
			_items.Add( title, script );
		}

		public void Attach( System.Web.UI.WebControls.WebControl ctl )
		{
			ctl.Attributes ["onclick"] = string.Format( "yaf_popit('{0}')", this.UniqueID );
			ctl.Attributes ["onmouseover"] = string.Format( "yaf_mouseover('{0}')", this.UniqueID );
		}

		private void Page_Load( object sender, System.EventArgs e )
		{
			/*
			if ( this.Visible )
			{
				Page.ClientScript.RegisterStartupScript( ClientID, string.Format( "<script language='javascript'>yaf_initmenu('{0}');</script>", ControlID ) );
			}
			*/
		}

		override protected void OnInit( EventArgs e )
		{
			this.Load += new System.EventHandler( this.Page_Load );
			this.PreRender += new EventHandler( PopMenu_PreRender );
			base.OnInit( e );
		}

		protected override void Render( System.Web.UI.HtmlTextWriter writer )
		{
		}

		private void PopMenu_PreRender( object sender, EventArgs e )
		{
			if ( !this.Visible )
				return;

			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			sb.AppendFormat( "<table width='1%' class='content' border=\"0\" cellspacing=\"0\" cellpadding=\"4\" id=\"{0}\" style=\"position:absolute;z-index:100;left:0;top:0;visibility:hidden;padding:0px;border:1px solid #FFFFFF;background-color:#FFFFFF\">", UniqueID );
			foreach ( string key in _items.Keys )
			{
				sb.AppendFormat( "<tr><td class='post' onmouseover=\"mouseHover(this,true)\" onmouseout=\"mouseHover(this,false)\" onclick=\"{1}\"><nobr>{0}</nobr></td></tr>\n", _items [key], Page.ClientScript.GetPostBackClientHyperlink( this, key ) );
			}
			sb.AppendFormat( "</table>" );

			Page.ClientScript.RegisterStartupScript( this.GetType(), ClientID + "_menuscript", sb.ToString() );
		}

		#region IPostBackEventHandler
		public event PopEventHandler ItemClick;

		public void RaisePostBackEvent( string eventArgument )
		{
			if ( ItemClick != null )
			{
				ItemClick( this, new PopEventArgs( eventArgument ) );
			}
		}
		#endregion
	}

	public class PopEventArgs : EventArgs
	{
		private string _item;

		public PopEventArgs( string eventArgument )
		{
			_item = eventArgument;
		}

		public string Item
		{
			get
			{
				return _item;
			}
		}
	}

	public delegate void PopEventHandler( object sender, PopEventArgs e );
}
