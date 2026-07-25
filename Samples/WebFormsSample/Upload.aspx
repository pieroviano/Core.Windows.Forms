<%@ Page Language="C#" %>
<script runat="server">
    protected void DoUpload (object sender, EventArgs e)
    {
        if (up.HasFile) {
            byte [] bytes = new byte [up.PostedFile.ContentLength];
            up.PostedFile.InputStream.Read (bytes, 0, bytes.Length);
            result.Text = String.Format ("name={0} len={1} type={2} sha={3} field={4}",
                up.FileName, up.PostedFile.ContentLength, up.PostedFile.ContentType,
                System.Convert.ToHexString (System.Security.Cryptography.SHA256.HashData (bytes)).Substring (0, 16),
                note.Text);
        } else {
            result.Text = "no file (Files.Count=" + Request.Files.Count + ")";
        }
    }
</script>
<!DOCTYPE html>
<html><body><form id="f" runat="server" enctype="multipart/form-data">
  <asp:FileUpload ID="up" runat="server" />
  <asp:TextBox ID="note" runat="server" />
  <asp:Button ID="go" runat="server" Text="Upload" OnClick="DoUpload" />
  <p><asp:Label ID="result" runat="server" /></p>
</form></body></html>
